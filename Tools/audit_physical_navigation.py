"""Audit completed Unity acquisition/development artifacts without running Unity.

Usage: python3 -B Tools/audit_physical_navigation.py --root ARTIFACT_ROOT --output REPORT
Reads Logs/navigation-integration-tests.json by default. Run after the Unity runner
writes its final receipt; a missing/incomplete receipt fails closed. Only public
observations are replayed. No world dataset, actuator mapping, or benchmark runs
are imported. Hashes bind local evidence; they are not remote attestation.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import importlib
import json
import math
import os
from pathlib import Path
import sys
import tempfile

sys.dont_write_bytecode = True
PROJECT = Path(__file__).resolve().parents[1]
if str(PROJECT) not in sys.path:
    sys.path.insert(0, str(PROJECT))
from Bridge.echo_source import load_core
DEFAULT_RECEIPT = PROJECT / "Logs/navigation-integration-tests.json"
EXPECTED_PROFILES = ["learn"] * 4 + ["trained"] * 3
WORK_LIMIT = 2_000_000
OBS_KEYS = set("schema session seq world profile width height x z goalX goalZ yaw completed rays outcome".split())
OUTCOME_KEYS = set("present session seq action command status fromX fromZ x z distanceMm".split())
RAY_KEYS = set("sx sz ex ez hit".split())


class AuditFailure(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise AuditFailure(message)


def integer(value, low, high, label):
    require(type(value) is int and low <= value <= high, "invalid integer: " + label)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def strict_json(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key: " + key)
            result[key] = value
        return result

    def constant(value):
        raise AuditFailure("nonfinite JSON: " + value)

    return json.loads(raw, object_pairs_hook=pairs, parse_constant=constant)


def empty_outcome():
    return dict(present=False, session="", seq=-1, action="", command=-1,
                status="", fromX=0, fromZ=0, x=0, z=0, distanceMm=0)


def projection(before, after, action):
    start = dict(schema="echo5.interaction.observation.v1", width=16, height=16,
                 walls=[], position=[before["z"], before["x"]], goal=[0, 0],
                 actions=[0, 1, 2, 3], tick=0)
    end = dict(start, position=[after["z"], after["x"]], tick=1)
    return dict(before=start, action=action, after=end)


def position(value):
    return value["x"], value["z"]


def validate_observation(obs):
    require(type(obs) is dict and set(obs) == OBS_KEYS, "non-public/missing observation fields")
    require(obs["schema"] == "echo.navigation.observation.v1", "observation schema")
    for key in ("world", "session"):
        require(type(obs[key]) is str and 0 < len(obs[key]) <= 128, "observation " + key)
    require(obs["profile"] in ("learn", "trained"), "unexpected physical profile")
    integer(obs["seq"], 0, 320, "sequence")
    for key in ("width", "height"):
        integer(obs[key], 5, 16, key)
    for key in ("x", "goalX"):
        integer(obs[key], 0, obs["width"] - 1, key)
    for key in ("z", "goalZ"):
        integer(obs[key], 0, obs["height"] - 1, key)
    integer(obs["yaw"], 0, 359, "yaw")
    require(type(obs["completed"]) is bool, "completed must be boolean")
    require(type(obs["rays"]) is list and len(obs["rays"]) == 61, "physical frame requires 61 depth rays")
    origin = None
    for ray in obs["rays"]:
        require(type(ray) is dict and set(ray) == RAY_KEYS, "non-public/missing ray fields")
        require(type(ray["hit"]) is bool, "ray hit must be boolean")
        for axis, size in (("x", obs["width"]), ("z", obs["height"])):
            integer(ray["s" + axis], -1000, size * 2000 - 1000, "ray origin")
            integer(ray["e" + axis], -13000, size * 2000 + 11000, "ray endpoint")
        require(math.hypot(ray["ex"] - ray["sx"], ray["ez"] - ray["sz"]) <= 10002,
                "physical ray exceeds 10m including millimetre rounding")
        here = ray["sx"], ray["sz"]
        require(origin is None or here == origin, "rays do not share a camera origin")
        origin = here
    require(abs(origin[0] - obs["x"] * 2000) <= 1001 and
            abs(origin[1] - obs["z"] * 2000) <= 1001, "camera origin disagrees with rounded odometry")
    if obs["completed"]:
        require(position(obs) == (obs["goalX"], obs["goalZ"]) and
                math.hypot(origin[0] - obs["goalX"] * 2000, origin[1] - obs["goalZ"] * 2000) <= 202,
                "completed claim disagrees with physical camera/goal pose")
    receipt = obs["outcome"]
    require(type(receipt) is dict and set(receipt) == OUTCOME_KEYS, "non-public/missing actuator receipt fields")
    require(type(receipt["present"]) is bool, "receipt present must be boolean")
    for key in ("seq", "command", "fromX", "fromZ", "x", "z", "distanceMm"):
        integer(receipt[key], -1 if key in ("seq", "command") else 0, 2**31 - 1, "receipt " + key)
    if not receipt["present"]:
        require(receipt == empty_outcome(), "invalid initial actuator receipt")
    else:
        require(receipt["action"] in ("move", "scan"), "invalid receipt action")
        if receipt["action"] == "move":
            integer(receipt["command"], 0, 3, "move command")
            require(receipt["status"] in ("arrived", "blocked", "cancelled"), "invalid move status")
        else:
            require(receipt["command"] == -1 and receipt["status"] in ("scanned", "cancelled")
                    and receipt["distanceMm"] == 0, "invalid scan receipt")


class Auditor:
    def __init__(self, root, integration, recorded_root=None):
        self.root = Path(root).expanduser().resolve()
        self.integration = Path(integration).expanduser().resolve()
        self.recorded_root = Path(recorded_root) if recorded_root is not None else None
        if self.recorded_root is not None:
            require(self.recorded_root.is_absolute() and '..' not in self.recorded_root.parts,
                    'recorded root must be an absolute path without traversal')
        self.inputs = {}
        self.report = dict(schema="echo.navigation.physical-audit.v1", valid=False,
                           root=str(self.root), integration=str(self.integration),
                           scope="Local physical receipts, depth/odometry projections and ECHO core evidence; acquisition/development only",
                           runs=[], errors=[], inputs=self.inputs)
        if self.recorded_root is not None:
            self.report['recordedRoot'] = str(self.recorded_root)

    def read(self, path, limit=32_000_000):
        path = Path(path).resolve()
        require(path.stat().st_size <= limit, "artifact too large: " + str(path))
        raw = path.read_bytes()
        self.inputs[str(path)] = dict(sha256=hashlib.sha256(raw).hexdigest(), bytes=len(raw))
        return raw

    def same_journal(self, claimed, path):
        require(type(claimed) is str and Path(claimed).is_absolute() and
                '..' not in Path(claimed).parts, "journal crosses artifact namespace")
        location = Path(claimed)
        if self.recorded_root is not None:
            require(location.is_relative_to(self.recorded_root), "journal crosses artifact namespace")
            # Relocate a reference, never read the original path or rewrite a journal.
            location = self.root / location.relative_to(self.recorded_root)
        require(location.resolve() == path and path.is_relative_to(self.root),
                "journal crosses artifact namespace")

    def load_core(self):
        source = load_core()
        interaction = importlib.import_module("echo5.interaction")
        require(Path(interaction.__file__).resolve() == source / "src/echo5/interaction.py", "different ECHO core already imported")
        self.Agent = interaction.HypothesisAgent
        self.Budget = importlib.import_module("echo5.budget").Budget
        self.verify_journal = importlib.import_module("echo5.trace").verify_journal
        self.report["coreSource"] = str(source)

    def model_matches(self, value, core):
        checkpoint = core.checkpoint()
        require(value["modelHash"] == checkpoint["sha256"], "modelHash does not match replayed core checkpoint")
        if "support" in value:
            require(canonical(value["support"]) == canonical(checkpoint["payload"]["model"]["support"]),
                    "support does not match replayed evidence")

    def journal(self, path):
        path = path.resolve()
        require(path.is_relative_to(self.root), "journal outside artifact root")
        raw = self.read(path)
        rows = [strict_json(line) for line in raw.splitlines()]
        verified = self.verify_journal(path)
        require(rows and rows[0]["kind"] == "navigation_session" and
                rows[-1]["kind"] == "navigation_finish", "journal is not a closed session: " + str(path))
        require(sum(row["kind"] == "navigation_session" for row in rows) == 1 and
                sum(row["kind"] == "navigation_finish" for row in rows) == 1, "duplicate session/finish event")
        allowed = {"navigation_session", "navigation_observation", "navigation_consequence",
                   "navigation_calibration", "navigation_plan", "navigation_decision",
                   "navigation_model_saved", "navigation_finish", "navigation_refutation"}
        require(all(row["kind"] in allowed for row in rows), "unexpected event (including map restore) in physical journal")
        return rows, verified

    def plans(self, events, decision, obs, core, failures):
        plans = [row for row in events if row["kind"] == "navigation_plan"]
        if not plans:
            require(not decision or not decision["plan"], "decision plan lacks a core planning event")
            return 0
        require(decision is not None, "planning during terminal ingestion")
        cells = decision["cells"]
        require(type(cells) is list, "decision cells missing")
        free, seen = set(), set()
        for cell in cells:
            require(set(cell) == {"x", "z", "state"}, "invalid decision cell")
            integer(cell["x"], 0, obs["width"] - 1, "cell x")
            integer(cell["z"], 0, obs["height"] - 1, "cell z")
            point = position(cell)
            require(point not in seen and cell["state"] in ("free", "occupied", "inferred"), "invalid/duplicate cell state")
            seen.add(point)
            if cell["state"] == "free":
                free.add(point)
        model = core.checkpoint()["payload"]["model"]
        for origin, action in failures:
            candidates = model["hypotheses"][action]
            if len(candidates) == 1 and model["support"][action]:
                dz, dx = candidates[0]
                free.discard((origin[0] + dx, origin[1] + dz))
        free.add(position(obs))
        planned = []
        for event in plans:
            self.model_matches(event, core)
            result = event["result"]
            require(type(result) is dict, "core plan result must be an object")
            require(result.get("certainty") != "conditional_on_declared_prior" and
                    "declared_prior" not in result.get("edge_sources", []), "declared effect prior in recorded plan")
            require(event["freeCells"] == len(free), "planning projection free-cell count mismatch")
            target = event["goal"]
            projected = dict(schema="echo5.interaction.observation.v1", width=obs["width"], height=obs["height"],
                             walls=[[z, x] for z in range(obs["height"]) for x in range(obs["width"]) if (x, z) not in free],
                             position=[obs["z"], obs["x"]], goal=[target["z"], target["x"]], actions=[0, 1, 2, 3], tick=0)
            replay = core.plan(projected, budget=self.Budget(WORK_LIMIT), max_depth=obs["width"] * obs["height"])
            require(canonical(result) == canonical(replay), "recorded plan differs from ECHO core.plan without priors")
            if result["status"] == "planned":
                planned.append(result)
        if planned:
            result = planned[-1]
            require(decision["action"] == "move" and decision["command"] == result["actions"][0] and
                    decision["plan"] == [dict(x=x, z=z) for z, x in result["positions"][1:]],
                    "actuator proposal is not linked to the core plan")
        else:
            require(not decision["plan"], "decision advertises an unrecorded plan")
        return len(plans)

    def session(self, run, path, rows, chain, checkpoint, first):
        start = rows[0]
        for key in ("session", "world", "profile"):
            require(start[key] == run[key], "Unity/journal identity mismatch: " + key)
        core = self.Agent() if first else self.Agent.restore(checkpoint, budget=self.Budget(WORK_LIMIT))
        require(start["restored"] is (not first), "first acquisition must be empty; subsequent sessions must restore")
        self.model_matches(start, core)
        if first:
            require(start["support"] == [0, 0, 0, 0] and not core.checkpoint()["payload"]["evidence"], "first learn is not zero support")
        indices = [i for i, event in enumerate(rows) if event["kind"] == "navigation_observation"]
        require(indices and indices[0] == 1 and len(indices) >= 2, "missing initial/terminal observations")
        counts = Counter(restored=int(not first))
        blocks = 0
        calibrations, saves, links = [], [], []
        failures = set()
        seen = {canonical(e) for e in core.checkpoint()["payload"]["evidence"]}
        previous = proposal = None
        total_plans = 0
        identity = None
        for index, offset in enumerate(indices):
            envelope = rows[offset]
            obs = envelope["observation"]
            validate_observation(obs)
            terminal = index == len(indices) - 1
            require(envelope["terminal"] is terminal, "terminal observation must be last")
            current_identity = tuple(obs[k] for k in ("session", "world", "profile", "width", "height", "goalX", "goalZ"))
            require(current_identity[:3] == (run["session"], run["world"], run["profile"]), "observation identity mismatch")
            require(identity is None or identity == current_identity, "session identity changed")
            identity = current_identity
            end = indices[index + 1] if not terminal else len(rows)
            events = rows[offset + 1:end]
            for event in events:
                require(event.get("session", run["session"]) == run["session"] and
                        event.get("seq", obs["seq"]) == obs["seq"], "event session/sequence mismatch")
                if event["kind"] == "navigation_refutation":
                    edge = event["edge"]
                    key = (tuple(edge["origin"]), edge["command"])
                    require(key in failures and event["source"] == "new_free_ray_measurement", "unlinked failed-edge refutation")
                    failures.remove(key)
            consequence = [e for e in events if e["kind"] == "navigation_consequence"]
            calibration = [e for e in events if e["kind"] == "navigation_calibration"]
            expected = dict(resolution="no_action", matched=False, confirmed=False)
            unit = False
            if previous is None:
                require(obs["seq"] == 0 and not obs["outcome"]["present"] and not obs["completed"], "invalid initial physical observation")
            elif obs["seq"] == previous["seq"]:
                require(terminal and obs == previous and proposal["action"] in ("done", "wait"), "same-seq finish changed data or leaves pending action")
            else:
                require(obs["seq"] == previous["seq"] + 1, "skipped/replayed observation sequence")
                receipt = obs["outcome"]
                if proposal["action"] in ("move", "scan"):
                    wanted = dict(present=True, session=run["session"], seq=proposal["seq"], action=proposal["action"],
                                  command=proposal["command"], fromX=previous["x"], fromZ=previous["z"], x=obs["x"], z=obs["z"])
                    require(all(type(receipt[k]) is type(v) and receipt[k] == v for k, v in wanted.items()), "actuator receipt not linked to proposal/actual pose")
                    if proposal["action"] == "scan":
                        require(position(obs) == position(previous) and (receipt["status"] == "cancelled" or obs["yaw"] == proposal["yaw"]), "scan changed pose or wrong yaw")
                        expected = dict(resolution="confirmed" if receipt["status"] == "scanned" else "cancelled", matched=True, confirmed=receipt["status"] == "scanned")
                    else:
                        unit = receipt["status"] == "arrived" and receipt["distanceMm"] <= 200 and sum(abs(a-b) for a, b in zip(position(previous), position(obs))) == 1
                        require(unit or (receipt["status"] != "arrived" and position(obs) == position(previous)), "unconfirmed/non-unit movement in successful run")
                        expected = dict(resolution="confirmed" if unit else "failed", matched=True, confirmed=unit)
                        counts["confirmedMoves" if unit else "failedMoves"] += 1
                        key = (position(previous), proposal["command"])
                        if unit:
                            failures.discard(key)
                            ray = obs["rays"][0]
                            residual = math.hypot(ray["sx"] - obs["x"] * 2000, ray["sz"] - obs["z"] * 2000)
                            require(residual <= receipt["distanceMm"] + 3, "arrival residual disagrees with camera odometry")
                        else:
                            failures.add(key)
                            blocks += int(receipt["status"] == "blocked")
                    require(len(consequence) == 1, "missing/duplicate physical consequence")
                    c = consequence[0]
                    require(canonical(c["outcome"]) == canonical(receipt) and c["previous_seq"] == proposal["seq"] and
                            all(type(c[k]) is type(v) and c[k] == v for k, v in expected.items()), "consequence does not match actual receipt")
                else:
                    require(position(obs) == position(previous) and receipt == previous["outcome"], "unsolicited physical outcome")
            if not expected["matched"]:
                require(not consequence, "unexpected consequence or duplicate evidence on same-seq finish")
            if unit and run["profile"] == "learn":
                require(len(calibration) == 1, "confirmed acquisition move lacks calibration evidence")
                event = calibration[0]
                record = projection(previous, obs, proposal["command"])
                require(all(canonical(event[k]) == canonical(v) for k, v in record.items()) and
                        canonical(event["outcome"]) == canonical(obs["outcome"]), "calibration projection not linked to physical unit move")
                signature = canonical(record)
                accepted = signature not in seen
                require(event["accepted"] is accepted, "duplicate physical projection inflated support or new projection discarded")
                if accepted:
                    learning = core.observe(record["before"], record["action"], record["after"], budget=self.Budget(WORK_LIMIT))
                    require(not learning["contradiction"] and not learning["out_of_model"], "contradictory physical actuator evidence")
                    seen.add(signature)
                    counts["calibrationAccepted"] += 1
                    calibrations.append(dict(record, outcome=obs["outcome"], learning=learning))
                    links.append(dict(observationIndex=offset, calibrationIndex=event["index"], proposalSeq=proposal["seq"], action=record["action"],
                                      before=record["before"]["position"], after=record["after"]["position"], evidenceIndex=len(seen)-1))
                else:
                    counts["duplicateProjections"] += 1
                self.model_matches(event, core)
            else:
                require(not calibration, "non-unit/frozen movement entered learning evidence")
                if unit:
                    model = core.checkpoint()["payload"]["model"]
                    dz, dx = obs["z"] - previous["z"], obs["x"] - previous["x"]
                    require(model["hypotheses"][proposal["command"]] == [[dz, dx]] and model["support"][proposal["command"]] >= 2,
                            "trained physical movement contradicts acquired model")
            decisions = [e for e in events if e["kind"] == "navigation_decision"]
            require(len(decisions) == (0 if terminal else 1), "missing/duplicate/terminal action proposal")
            decision = None if terminal else decisions[0]["decision"]
            if decision is not None:
                require(decision["schema"] == "echo.navigation.decision.v1" and decision["session"] == run["session"] and
                        decision["seq"] == obs["seq"], "decision identity mismatch")
                require(decision["action"] in ("move", "scan", "done", "wait"), "invalid decision action")
                integer(decision["command"], 0 if decision["action"] == "move" else -1, 3 if decision["action"] == "move" else -1, "decision command")
                self.model_matches(decision, core)
                self.same_journal(decision["journal"], path)
                model = core.checkpoint()["payload"]["model"]
                learned = sum(s >= 2 and len(h) == 1 for s, h in zip(model["support"], model["hypotheses"]))
                require(decision["learnedActions"] == learned, "learnedActions exceeds saved evidence")
                counts["decisions"] += 1
                if decision["action"] in ("move", "scan"):
                    counts["moves" if decision["action"] == "move" else "scans"] += 1
                require(decision["action"] != "done" or obs["completed"], "done without physical goal success")
            total_plans += self.plans(events, decision, obs, core, failures)
            for event in events:
                if event["kind"] == "navigation_model_saved":
                    require(run["profile"] == "learn" and obs["completed"] and calibrations and not saves, "invalid/duplicate model save")
                    self.model_matches(event, core)
                    require(event["world"] == run["world"] and event["observations"] == len(core.checkpoint()["payload"]["evidence"]), "model save evidence count/identity mismatch")
                    saves.append(dict(session=run["session"], world=run["world"], completed=True, journal=str(path),
                                      calibrations=list(calibrations), modelHash=core.checkpoint()["sha256"], metrics=dict(counts)))
            previous = obs
            if decision is not None:
                proposal = decision
        finish = rows[-1]["result"]
        require(finish["schema"] == "echo.navigation.finish.v1" and finish["status"] == "closed" and
                finish["session"] == run["session"] and finish["seq"] == previous["seq"] and
                finish["completed"] is True and finish["pending"] is False and previous["completed"], "missing closed actual-success metadata")
        require(all(type(finish[k]) is type(v) and finish[k] == v for k, v in expected.items()), "finish consequence mismatch")
        require(finish["modelSaved"] is bool(saves), "finish modelSaved disagrees with save journal")
        self.model_matches(finish, core)
        self.same_journal(finish["journal"], path)
        for key in ("restored", "decisions", "moves", "scans", "confirmedMoves", "failedMoves", "calibrationAccepted", "duplicateProjections"):
            require(type(finish["metrics"].get(key, 0)) is int and finish["metrics"].get(key, 0) == counts[key], "terminal metric mismatch: " + key)
        for event in rows:
            if "budget" in event:
                budget = event["budget"]
                require(budget["limit"] == WORK_LIMIT and budget["used"] + budget["remaining"] == WORK_LIMIT and
                        0 <= budget["used"] <= WORK_LIMIT and sum(budget["counts"].values()) == budget["used"], "invalid work budget")
        require(0 < counts["decisions"] <= 320, "decision budget exceeded")
        for key in ("valid", "success", "terminalAcknowledged", "terminalCompleted"):
            require(run[key] is True, "Unity did not confirm " + key)
        require(run["terminalPending"] is False and run["modelSaved"] is bool(saves), "Unity terminal/save mismatch")
        for key, value in (("moves", counts["moves"]), ("scans", counts["scans"]), ("blocks", blocks),
                           ("actions", finish["seq"]), ("rays", 61), ("learnedActions", 4)):
            require(type(run[key]) is int and run[key] == value, "Unity counter mismatch: " + key)
        require(run["actions"] == run["moves"] + run["scans"], "unexecuted proposal counted as actual action")
        self.model_matches(run, core)
        require(all(s >= 2 and len(h) == 1 for s, h in zip(core.checkpoint()["payload"]["model"]["support"], core.hypotheses)), "four independently supported singleton effects required")
        if first:
            require(len(calibrations) >= 8 and counts["confirmedMoves"] >= 8 and len(saves) == 1, "first learn needs >=8 actual independent moves and a saved model")
        if run["profile"] == "trained":
            require(core.checkpoint() == checkpoint and total_plans > 0, "trained model changed or ECHO core planner unused")
        summary = dict(session=run["session"], world=run["world"], profile=run["profile"], journal=str(path),
                       chain=chain, initialModelHash=start["modelHash"], modelHash=finish["modelHash"],
                       initialSupport=start["support"], support=finish["support"], confirmedUnitMoves=counts["confirmedMoves"],
                       acceptedProjections=len(calibrations), duplicateProjections=counts["duplicateProjections"],
                       plansReplayedWithoutPrior=total_plans, modelSaved=bool(saves), closedActualSuccess=True,
                       evidenceLinks=links, blocks=blocks)
        return core.checkpoint(), saves, summary

    def run(self):
        context = "inputs"
        try:
            require(self.root.is_dir(), "artifact root missing")
            receipt = strict_json(self.read(self.integration))
            require(receipt["valid"] is True, "Unity integration receipt is incomplete or failed")
            runs = receipt["runs"]
            require([r["profile"] for r in runs] == EXPECTED_PROFILES, "expected four acquisition and three development runs")
            require(len({r["session"] for r in runs}) == len(runs) and len({r["world"] for r in runs}) == len(runs), "duplicate Unity session/world")
            self.load_core()
            journals = {}
            for path in sorted(self.root.glob("run-*/thought.jsonl")):
                context = str(path)
                rows, chain = self.journal(path)
                token = rows[0]["session"]
                require(token not in journals, "duplicate backend session journal")
                journals[token] = path.resolve(), rows, chain
            require(set(journals) == {r["session"] for r in runs}, "Unity sessions do not exactly match artifact namespace")
            model_path = self.root / "model.json"
            model = strict_json(self.read(model_path))
            require(set(model) == {"schema", "checkpoint", "acquisition"} and model["schema"] == "echo.navigation.model.v1", "saved model envelope schema")
            restored = self.Agent.restore(model["checkpoint"], budget=self.Budget(WORK_LIMIT)).checkpoint()
            require(restored == model["checkpoint"] and restored["payload"]["mode"] == "active", "core.restore changed saved checkpoint")
            saved_checkpoint, acquisition = None, []
            for index, run in enumerate(runs):
                context = "session " + run["session"]
                path, rows, chain = journals[run["session"]]
                checkpoint, saves, summary = self.session(run, path, rows, chain, saved_checkpoint, index == 0)
                if saves:
                    saved_checkpoint = checkpoint
                    acquisition.extend(saves)
                else:
                    require(checkpoint == saved_checkpoint, "unsaved acquisition model diverged")
                self.report["runs"].append(summary)
            context = "saved acquisition provenance"
            require(saved_checkpoint == model["checkpoint"], "final saved model differs from physically acquired checkpoint")
            require(len(acquisition) == len(model["acquisition"]), "saved acquisition provenance missing/extra sessions")
            for expected, actual in zip(acquisition, model["acquisition"]):
                require(set(actual) == set(expected) | {"projection"}, "acquisition provenance schema")
                for key, value in expected.items():
                    if key == "journal":
                        self.same_journal(actual[key], Path(value))
                    elif key == "metrics":
                        require(all(actual[key].get(k, 0) == v for k, v in value.items()), "saved acquisition metrics mismatch")
                    else:
                        require(canonical(actual[key]) == canonical(value), "saved acquisition not linked to actual journal: " + key)
            require(runs[4]["obstacleInjected"] is True and self.report["runs"][4]["blocks"] >= 1, "development intervention lacks an actual blocked receipt")
            final_hash = saved_checkpoint["sha256"]
            require(all(r["initialModelHash"] == final_hash and r["modelHash"] == final_hash for r in self.report["runs"][4:]), "trained sessions did not restore final model in this namespace")
            # Fingerprint the actual imported core/legacy modules and local adapter.
            sources = {Path(__file__).resolve(), PROJECT / "Bridge/navigation.py"}
            for name, module in list(sys.modules.items()):
                filename = getattr(module, "__file__", None)
                if filename and (name.startswith("echo5.") or name.split(".")[0] in ("echoai", "nexus0", "seed", "echo4", "echo_arc")):
                    sources.add(Path(filename).resolve())
            for path in sorted(sources):
                self.read(path)
            self.report["sources"] = {str(p): self.inputs[str(p)]["sha256"] for p in sorted(sources)}
            context = "stable artifact snapshot"
            for path, fingerprint in self.inputs.items():
                require(hashlib.sha256(Path(path).read_bytes()).hexdigest() == fingerprint["sha256"], "input changed during audit: " + path)
            self.report.update(valid=True, modelHash=final_hash, modelFileSha256=self.inputs[str(model_path)]["sha256"],
                               support=saved_checkpoint["payload"]["model"]["support"],
                               evidenceCount=len(saved_checkpoint["payload"]["evidence"]), coreRestoreExact=True,
                               firstLearnZeroSupport=True, firstLearnModelSaved=True, trainedModelFrozen=True,
                               plansReplayedWithoutPriors=sum(r["plansReplayedWithoutPrior"] for r in self.report["runs"]))
        except (OSError, ValueError, TypeError, KeyError, IndexError, AttributeError, RuntimeError, ImportError) as error:
            self.report["errors"].append(dict(context=context, type=type(error).__name__, message=str(error)))
        self.report["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        return self.report


def audit(root, integration=DEFAULT_RECEIPT, recorded_root=None):
    return Auditor(root, integration, recorded_root).run()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True, help="fresh physical bridge artifact namespace")
    parser.add_argument("--output", type=Path, required=True, help="JSON audit report")
    parser.add_argument("--integration", type=Path, default=DEFAULT_RECEIPT, help="completed Unity integration receipt")
    parser.add_argument("--recorded-root", type=Path, help="original absolute namespace of an unchanged relocated archive")
    args = parser.parse_args(argv)
    report = audit(args.root, args.integration, args.recorded_root)
    output = args.output.expanduser().resolve()
    if str(output) in report["inputs"] or output == (args.root / "model.json").resolve() or output == args.integration.resolve():
        parser.error("--output must not overwrite audit evidence")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=output.parent, prefix=output.name + ".", suffix=".tmp", delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n")
    temporary.replace(output)
    print(json.dumps(dict(valid=report["valid"], output=str(output), errors=report["errors"])))
    return 0 if report["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
