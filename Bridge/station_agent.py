"""Public Unity observations -> declared mission adapter -> existing ECHO-5 PLAN.

No world object, hidden map, learned mission rules, model, or second thought bus.
Unknown cells are excluded in a temporary planner projection, never stored as walls.
"""

from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import threading
import uuid

# Reading the installed source must not create bytecode in the frozen project.
sys.dont_write_bytecode = True
from echo_source import load_core
ECHO5_SOURCE = load_core()
from echo5.budget import Budget, BudgetExceeded
from echo5.interaction import HypothesisAgent
from echo5.trace import Recorder

if Path(sys.modules[HypothesisAgent.__module__].__file__).resolve() != ECHO5_SOURCE / "src/echo5/interaction.py":
    raise RuntimeError("another ECHO-5 installation is already imported")

MAX_BODY = 1_048_576
DEFAULT_ARTIFACTS = Path(__file__).resolve().parents[1] / "Artifacts/Unity"
EFFECT_PRIOR = ((0, 1), (0, -1), (1, 0), (-1, 0))  # ECHO rows=z, columns=x.
KINDS = frozenset(("key", "battery", "door", "pump", "valve", "charger",
                   "parcel", "delivery", "terminal", "scenery"))
PICKUPS = frozenset(("key", "battery", "parcel"))
OBS_KEYS = frozenset("schema session seq mission width height x z battery inventory cells objects lastResult completed outcome".split())
OBJECT_KEYS = frozenset("id kind label x z inspected compatible active locked".split())
OUTCOME_KEYS = frozenset("present session seq action objectId status targetX targetZ actualX actualZ distanceMm".split())
SOURCE = "ECHO-5 HypothesisAgent.plan; declared Unity symbolic adapter"


class ProtocolError(ValueError):
    def __init__(self, status, code):
        super().__init__(code)
        self.status = status
        self.code = code


def _require(condition, code="invalid_payload", status=400):
    if not condition:
        raise ProtocolError(status, code)


def _integer(value, low, high):
    _require(type(value) is int and low <= value <= high)


def _text(value, maximum, *, identifier=False):
    _require(type(value) is str and len(value) <= maximum)
    _require(not any(ord(c) < 32 and c not in "\n\r\t" for c in value))
    try:
        value.encode("utf-8")
    except UnicodeError as exc:
        raise ProtocolError(400, "invalid_unicode") from exc
    if identifier:
        _require(bool(re.fullmatch(r"[A-Za-z0-9_.:-]{1,128}", value)))


def _fields(value, keys):
    _require(type(value) is dict and set(value) == set(keys), "invalid_fields")


def _point(value):
    return value["x"], value["z"]


def _distance(a, b):
    return abs(a[0] - b[0]) + abs(a[1] - b[1])


def _xy(point):
    return dict(x=point[0], z=point[1])


def _validate_outcome(outcome, width, height):
    _fields(outcome, OUTCOME_KEYS)
    _require(type(outcome["present"]) is bool)
    for field in ("session", "action", "objectId", "status"):
        _text(outcome[field], 128)
    _integer(outcome["seq"], -1, 2**31 - 1)
    for field in ("targetX", "actualX"):
        _integer(outcome[field], 0, width - 1)
    for field in ("targetZ", "actualZ"):
        _integer(outcome[field], 0, height - 1)
    _integer(outcome["distanceMm"], 0, 2**31 - 1)
    if not outcome["present"]:
        _require(outcome["seq"] == -1 and
                 all(outcome[k] == "" for k in ("session", "action", "objectId", "status")) and
                 all(outcome[k] == 0 for k in ("targetX", "targetZ", "actualX", "actualZ", "distanceMm")),
                 "invalid_empty_outcome")
        return
    _text(outcome["session"], 128, identifier=True)
    if outcome["objectId"]:
        _text(outcome["objectId"], 128, identifier=True)
    _integer(outcome["seq"], 0, 2**31 - 1)
    _require(outcome["action"] in ("move", "inspect", "interact"), "invalid_outcome_action")
    statuses = ("arrived", "blocked", "cancelled", "rejected") if outcome["action"] == "move" else ("applied", "rejected")
    _require(outcome["status"] in statuses, "invalid_outcome_status")
    if outcome["action"] != "move":
        _require(outcome["distanceMm"] == 0, "interaction_distance_must_be_zero")


def validate_observation(value):
    """Exact schema and bounded fields; JSON booleans are not integers."""
    _fields(value, OBS_KEYS)
    _require(value["schema"] == "echo.station.observation.v1", "unsupported_schema")
    _text(value["session"], 128, identifier=True)
    _integer(value["seq"], 0, 2**31 - 1)
    _require(value["mission"] in ("irrigation", "delivery", "detour"))
    _integer(value["width"], 2, 15)
    _integer(value["height"], 2, 13)
    width, height = value["width"], value["height"]
    _integer(value["x"], 0, width - 1)
    _integer(value["z"], 0, height - 1)
    _integer(value["battery"], 0, 100)
    _require(type(value["completed"]) is bool)
    _text(value["lastResult"], 4096)
    _validate_outcome(value["outcome"], width, height)
    inventory = value["inventory"]
    _require(type(inventory) is list and len(inventory) <= 3)
    _require(all(type(v) is str and v in PICKUPS for v in inventory))
    for field in ("cells", "objects"):
        _require(type(value[field]) is list and len(value[field]) <= width * height)
    coords = set()
    for cell in value["cells"]:
        _fields(cell, ("x", "z", "blocked"))
        _integer(cell["x"], 0, width - 1)
        _integer(cell["z"], 0, height - 1)
        _require(type(cell["blocked"]) is bool)
        _require(_point(cell) not in coords, "duplicate_cell")
        _require(_point(cell) != _point(value) or not cell["blocked"], "occupied_position_blocked")
        coords.add(_point(cell))
    ids = set()
    for item in value["objects"]:
        _fields(item, OBJECT_KEYS)
        _text(item["id"], 128, identifier=True)
        _text(item["label"], 256)
        _require(type(item["kind"]) is str and item["kind"] in KINDS)
        _require(item["id"] not in ids, "duplicate_object")
        ids.add(item["id"])
        _integer(item["x"], 0, width - 1)
        _integer(item["z"], 0, height - 1)
        for field in ("inspected", "compatible", "active", "locked"):
            _require(type(item[field]) is bool)
    return value


def decode_observation(raw):
    _require(type(raw) is bytes)
    _require(len(raw) <= MAX_BODY, "body_too_large", 413)

    def pairs(items):
        result = {}
        for key, value in items:
            _require(key not in result, "duplicate_json_key")
            result[key] = value
        return result

    def constant(_):
        raise ProtocolError(400, "nonfinite_json_number")

    try:
        return validate_observation(json.loads(raw.decode("utf-8"),
                                              object_pairs_hook=pairs, parse_constant=constant))
    except (UnicodeError, ValueError, TypeError, RecursionError) as exc:
        if isinstance(exc, ProtocolError):
            raise
        raise ProtocolError(400, "invalid_json") from exc


class StationAgent:
    """A bounded symbolic adapter. All paths come from HypothesisAgent.plan."""

    def __init__(self, *, budget, recorder, journal_path):
        self.budget = budget
        self.recorder = recorder
        self.journal_path = str(journal_path)
        # The planner's projected exclusions are not factual wall observations.
        # Record its returned route below, not its synthetic input as world truth.
        self.core = HypothesisAgent()
        self.cells = {}
        self.objects = {}
        self.collected = set()
        self.visits = Counter()
        self.key_acquired = False
        self.charging = False
        self.last_observation = None
        self.last_decision = None
        self.failure_count = 0
        self.stopped = False
        self.outcome_unresolved = False

    def check_observation(self, obs):
        _require(len(set(self.objects) | {o["id"] for o in obs["objects"]}) <= obs["width"] * obs["height"],
                 "object_history_limit", 409)
        for item in obs["objects"]:
            old = self.objects.get(item["id"])
            if old:
                _require((old["kind"], old["x"], old["z"]) ==
                         (item["kind"], item["x"], item["z"]), "object_identity_changed", 409)

    def _remember(self, obs):
        for cell in obs["cells"]:
            self.cells[_point(cell)] = cell["blocked"]
        for public in obs["objects"]:
            old = self.objects.get(public["id"], {})
            item = dict(public)
            # Compatibility has no evidential meaning until inspected.
            item["compatible"] = (public["compatible"] if public["inspected"]
                                  else old.get("compatible", False))
            item["inspected"] = public["inspected"] or old.get("inspected", False)
            item["active"] = public["active"] or old.get("active", False)
            self.objects[item["id"]] = item
            if item["kind"] in PICKUPS and item["active"]:
                self.collected.add(item["id"])
        self.key_acquired |= "key" in obs["inventory"] or any(
            o["kind"] == "key" and o["id"] in self.collected for o in self.objects.values())
        self.visits[_point(obs)] += 1

    def _free(self, obs):
        free = {p for p, blocked in self.cells.items() if not blocked}
        for o in self.objects.values():
            if o["id"] in self.collected:
                continue
            # Key is walkable in StationWorld. Open door terrain must also be observed free.
            if o["kind"] in ("charger", "delivery", "key"):
                continue
            if o["kind"] == "door" and not o["locked"]:
                continue
            free.discard(_point(o))
        # Position is public, but do not invent a CellData entry for it.
        free.add(_point(obs))
        return free

    def _consequence(self, obs):
        previous = self.last_decision
        if previous is None or previous["action"] not in ("move", "inspect", "interact"):
            # An initial/manual receipt has no proposal here to which it can be credited.
            return dict(resolution="no_action", matched=False, confirmed=False)
        before = self.last_observation
        action, identifier = previous["action"], previous["objectId"]
        # Confirmation requires current public facts, not an out-of-view remembered flag.
        item = next((o for o in obs["objects"] if o["id"] == identifier), None)
        remembered = self.objects.get(identifier)
        outcome = obs["outcome"]
        expected = dict(session=previous["session"], seq=previous["seq"], action=action,
                        objectId=identifier, targetX=previous["x"], targetZ=previous["z"],
                        actualX=obs["x"], actualZ=obs["z"])
        mismatches = [key for key, value in expected.items() if outcome[key] != value]
        matched = outcome["present"] and not mismatches
        success = False
        explained_obstacle = False
        if matched and action == "move":
            success = (outcome["status"] == "arrived" and outcome["distanceMm"] <= 200
                       and _point(obs) == _point(previous))
            explained_obstacle = _point(previous) not in self._free(obs)
            if not success and _point(obs) != _point(before):
                self.stopped = True
        elif matched and outcome["status"] == "applied":
            if action == "inspect":
                success = bool(item and item["inspected"])
            elif remembered:
                kind = remembered["kind"]
                if kind in PICKUPS:
                    success = (obs["inventory"].count(kind) > before["inventory"].count(kind)
                               or bool(item and item["active"]))
                    if success:
                        self.collected.add(identifier)
                        if kind == "key":
                            self.key_acquired = True
                elif kind == "charger":
                    success = obs["battery"] > before["battery"] or obs["battery"] == 100
                elif kind == "door":
                    success = bool(item and not item["locked"])
                else:
                    success = bool(item and item["active"])
        if matched:
            self.failure_count = 0 if success or explained_obstacle else self.failure_count + 1
            self.stopped |= self.failure_count >= 2
            resolution = "confirmed" if success else "failed"
            detail = "receipt_and_current_facts_confirmed" if success else "receipt_or_current_facts_do_not_confirm"
        else:
            self.outcome_unresolved = True
            resolution = "unresolved"
            detail = "outcome_missing" if not outcome["present"] else "outcome_mismatch:" + ",".join(mismatches)
        result = dict(resolution=resolution, matched=matched, confirmed=success)
        self.recorder.write(kind="station_consequence", session=obs["session"], seq=obs["seq"],
                            previous_seq=previous["seq"], action=action, objectId=identifier,
                            **result, detail=detail, outcome=outcome, observed_obstacle=explained_obstacle,
                            position=_xy(_point(obs)), battery=obs["battery"],
                            inventory=obs["inventory"], lastResult=obs["lastResult"])
        return result

    def _route(self, obs, goal):
        start, free = _point(obs), self._free(obs)
        if goal not in free:
            return None
        projected = dict(schema="echo5.interaction.observation.v1", width=obs["width"],
                         height=obs["height"], position=[start[1], start[0]],
                         goal=[goal[1], goal[0]], actions=[0, 1, 2, 3], tick=0,
                         walls=[[z, x] for z in range(obs["height"]) for x in range(obs["width"])
                                if (x, z) not in free])
        result = self.core.plan(projected, budget=self.budget,
                                max_depth=obs["width"] * obs["height"], effect_prior=EFFECT_PRIOR)
        self.recorder.write(kind="station_plan", session=obs["session"], seq=obs["seq"],
                            goal=_xy(goal), known_free=len(free),
                            unknown_excluded=obs["width"] * obs["height"] - len(self.cells),
                            motor_prior="declared public calibration; not learned",
                            result=result)
        if result.get("reason") == "budget_exhausted":
            raise BudgetExceeded("PLAN exhausted logical budget")
        if result["status"] == "reached":
            return []
        if result["status"] != "planned":
            return None
        return [(col, row) for row, col in result["positions"][1:]]

    def _decision(self, obs, action, phase, reason, *, item=None, route=None):
        route = route or []
        destination = route[0] if action == "move" else (_point(item) if item else _point(obs))
        prediction = "Await a new public observation; no physical outcome asserted."
        if action == "move":
            prediction = f"Declared motor prior predicts arrival at ({destination[0]},{destination[1]}); Unity must confirm."
        elif action == "inspect":
            prediction = "Public inspected flag should become true; compatibility remains unverified until observed."
        elif action == "interact":
            prediction = "Declared interaction preconditions hold; verify inventory, active/locked flags or charge next."
        return dict(schema="echo.station.decision.v1", session=obs["session"], seq=obs["seq"],
                    action=action, x=destination[0], z=destination[1],
                    objectId=item["id"] if item else "", reason=reason, prediction=prediction,
                    hypotheses=["Declared public motor prior in (row=z,column=x): ((0,1),(0,-1),(1,0),(-1,0)); not learned.",
                                "Mission preconditions are a declared symbolic adapter.",
                                "Unobserved cells are unknown and excluded from routing; remembered cells may become stale."],
                    evidence=[f"Public position=({obs['x']},{obs['z']}), battery={obs['battery']}, inventory={obs['inventory']}.",
                              f"Observed cells this request={len(obs['cells'])}; remembered cells={len(self.cells)}.",
                              f"Public lastResult: {obs['lastResult']}"] +
                             ([f"Object {item['id']}: kind={item['kind']}, inspected={item['inspected']}, active={item['active']}, locked={item['locked']}."] if item else []),
                    budgetUsed=self.budget.used, budgetRemaining=self.budget.remaining,
                    knownCells=[dict(x=x, z=z, blocked=blocked)
                                for (x, z), blocked in sorted(self.cells.items())],
                    plan=[_xy(p) for p in route], source=SOURCE,
                    journal=self.journal_path, phase=phase)

    def _items(self, kind):
        return [o for o in self.objects.values() if o["kind"] == kind and o["id"] not in self.collected]

    def _targets(self, obs, item, *, exact=False):
        free, origin = self._free(obs), _point(obs)
        x, z = _point(item)
        targets = [(x, z)] if exact else [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)]
        if not exact and item["kind"] in ("charger", "delivery", "key"):
            targets.append((x, z))
        return sorted((p for p in targets if p in free), key=lambda p: (_distance(origin, p), p))

    def _approach(self, obs, items, phase, reason, *, action="interact", exact=False, energy_check=False):
        origin = _point(obs)
        for item in sorted(items, key=lambda o: (_distance(origin, _point(o)), o["id"])):
            for target in self._targets(obs, item, exact=exact):
                route = self._route(obs, target)
                if route is None:
                    continue
                # World reports ceil(Energy). Reserve 0.12 to dock and 0.28 per move.
                if energy_check and route and len(route) * 28 + 12 > max(0, obs["battery"] - 1) * 100:
                    continue
                if route:
                    return self._decision(obs, "move", phase, reason, item=item, route=route)
                return self._decision(obs, action, phase, reason, item=item)
        return None

    def _explore(self, obs):
        origin, free = _point(obs), self._free(obs)
        # Choosing an observed frontier is a declared exploration heuristic.
        # There is no path search here: ECHO-5 alone decides reachability and routes.
        candidates = []
        for x, z in free:
            if (x, z) == origin:
                continue
            if any(0 <= nx < obs["width"] and 0 <= nz < obs["height"] and (nx, nz) not in self.cells
                   for nx, nz in ((x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1))):
                candidates.append((x, z))
        for target in sorted(candidates, key=lambda p: (self.visits[p], _distance(origin, p), p)):
            route = self._route(obs, target)
            if route:
                return self._decision(obs, "move", "explore", "Seek more public observations from a known-free frontier.", route=route)
        return self._decision(obs, "wait", "no_known_route", "No reachable known-free frontier or mission target; hidden cells remain unknown.")

    def _choose(self, obs):
        if obs["completed"]:
            return self._decision(obs, "done", "completed", "Unity reports completed=true.")
        if self.outcome_unresolved:
            return self._decision(obs, "wait", "outcome_unresolved", "Missing or mismatched action receipt; execution is unresolved. Resolve it before starting a new session.")
        if self.stopped:
            return self._decision(obs, "wait", "actuator_failure", "Unmodeled action outcome; one retry exhausted. Start a new session after resolving the actuator.")
        self.budget.consume("station_decision")
        chargers = self._items("charger")
        self.charging = obs["battery"] < 25 or (self.charging and obs["battery"] < 100)
        if self.charging:
            result = self._approach(obs, chargers, "recharge", "Public charge below reserve; recharge before resuming the mission.", energy_check=True)
            return result or self._decision(obs, "wait", "energy_blocked", "No known charger route affordable under declared Unity energy costs.")

        inventory = obs["inventory"]
        if obs["mission"] == "delivery":
            delivery = self._items("delivery")
            if any(o["active"] for o in delivery):
                return self._return(obs, chargers)
            if "parcel" in inventory:
                result = self._approach(obs, delivery, "deliver", "Public inventory contains parcel; seek delivery terminal.")
            elif len(inventory) < 3:
                result = self._approach(obs, self._items("parcel"), "collect_parcel", "Declared delivery precondition: collect the observed parcel.")
            else:
                return self._decision(obs, "wait", "inventory_full", "Three inventory slots occupied; no declared drop action.")
        else:
            pumps, valves = self._items("pump"), self._items("valve")
            if any(o["active"] for o in valves) and any(o["active"] for o in pumps):
                return self._return(obs, chargers)
            if any(o["active"] for o in pumps):
                result = self._approach(obs, [o for o in valves if not o["active"]], "open_valve", "Remembered public pump active=true satisfies valve precondition.")
            elif not self.key_acquired:
                if len(inventory) >= 3:
                    return self._decision(obs, "wait", "inventory_full", "Key required, but all three inventory slots are occupied.")
                result = self._approach(obs, self._items("key"), "collect_key", "Declared irrigation precondition: collect a service key.")
            elif "battery" not in inventory:
                if len(inventory) >= 3:
                    return self._decision(obs, "wait", "inventory_full", "Battery required, but all three inventory slots are occupied.")
                batteries = self._items("battery")
                result = self._approach(obs, [o for o in batteries if o["inspected"] and o["compatible"]],
                                        "collect_battery", "Observed inspection confirms compatible battery.")
                if result is None:
                    result = self._approach(obs, [o for o in batteries if not o["inspected"]],
                                            "inspect_battery", "Battery compatibility is unknown until inspected.", action="inspect")
            else:
                result = self._approach(obs, [o for o in self._items("door") if o["locked"]],
                                        "unlock_door", "Public inventory contains key; unlock the observed door.") if "key" in inventory else None
                if result is None:
                    result = self._approach(obs, [o for o in pumps if not o["active"]],
                                            "power_pump", "Public inventory contains the inspected compatible battery.")
        return result or self._explore(obs)

    def _return(self, obs, chargers):
        return (self._approach(obs, chargers, "return_charger", "Public mission effects confirmed; return to observed charger.", exact=True)
                or self._explore(obs))

    def decide(self, obs):
        self.recorder.write(kind="station_observation", observation=obs)
        self._remember(obs)
        self._consequence(obs)
        try:
            self.budget.consume("station_observation", 1 + len(obs["cells"]) + len(obs["objects"]))
            result = self._choose(obs)
        except BudgetExceeded:
            result = self._decision(obs, "wait", "budget_exhausted", "ECHO-5 logical work budget exhausted; no action authorized.")
        self.recorder.write(kind="station_decision", decision=result, budget=self.budget.snapshot())
        self.last_observation, self.last_decision = obs, result
        return result

    def finish(self, obs):
        """Record the final observation and matched consequence; never propose an action."""
        self.recorder.write(kind="station_observation", terminal=True, observation=obs)
        self._remember(obs)
        result = self._consequence(obs)
        self.recorder.write(kind="station_finish", session=obs["session"], seq=obs["seq"],
                            previous_seq=self.last_decision["seq"], **result,
                            pending=result["resolution"] == "unresolved", budget=self.budget.snapshot())
        return dict(schema="echo.station.finish.v1", session=obs["session"], seq=obs["seq"],
                    status="closed", **result, journal=self.journal_path)


@dataclass
class Session:
    mission: str
    size: tuple
    agent: StationAgent
    recorder: Recorder
    seq: int = -1
    fingerprint: bytes = b""
    response: bytes = b""
    requests: int = 0
    failed: bool = False
    closed: bool = False
    finish_fingerprint: bytes = b""
    finish_response: bytes = b""


class BridgeService:
    """Serialized requests; latest exact-byte retries are free and side-effect free.

    No eviction: restarting an evicted session could execute an old action again.
    The cap bounds both retained state and open journals until service restart.
    """

    def __init__(self, *, artifact_root=DEFAULT_ARTIFACTS, budget_limit=1_000_000,
                 max_sessions=16, max_requests=400):
        _integer(budget_limit, 0, 1_000_000)
        _integer(max_sessions, 1, 64)
        _integer(max_requests, 1, 400)
        self.artifact_root = Path(artifact_root).resolve()
        self.budget_limit = budget_limit
        self.max_sessions, self.max_requests = max_sessions, max_requests
        self.sessions = {}
        self.lock = threading.Lock()

    def process(self, raw):
        obs = decode_observation(raw)
        fingerprint = hashlib.sha256(raw).digest()
        with self.lock:
            session = self.sessions.get(obs["session"])
            if session:
                _require(not session.closed, "session_closed", 409)
                _require(not session.failed, "session_failed_restart_required", 503)
                if obs["seq"] == session.seq:
                    _require(fingerprint == session.fingerprint, "changed_retry", 409)
                    return session.response
                _require(obs["seq"] == session.seq + 1, "stale_or_skipped_sequence", 409)
                _require(obs["mission"] == session.mission and
                         (obs["width"], obs["height"]) == session.size, "session_scenario_changed", 409)
                _require(session.requests < self.max_requests, "request_limit", 429)
                session.agent.check_observation(obs)
            else:
                _require(len(self.sessions) < self.max_sessions, "session_limit", 503)
                run = "run-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex
                journal = self.artifact_root / run / "thought.jsonl"
                recorder = Recorder(journal)
                agent = StationAgent(budget=Budget(self.budget_limit), recorder=recorder, journal_path=journal)
                session = Session(obs["mission"], (obs["width"], obs["height"]), agent, recorder)
                self.sessions[obs["session"]] = session
            try:
                result = session.agent.decide(obs)
                response = json.dumps(result, ensure_ascii=True, allow_nan=False, separators=(",", ":")).encode("utf-8")
            except Exception:
                # No replay after a partially recorded decision or journal I/O failure.
                session.failed = True
                raise
            session.seq, session.fingerprint, session.response = obs["seq"], fingerprint, response
            session.requests += 1
            return response

    def finish(self, raw):
        obs = decode_observation(raw)
        fingerprint = hashlib.sha256(raw).digest()
        with self.lock:
            session = self.sessions.get(obs["session"])
            _require(session is not None, "unknown_session", 404)
            if session.closed:
                _require(fingerprint == session.finish_fingerprint, "changed_finish", 409)
                return session.finish_response
            _require(not session.failed, "session_failed_restart_required", 503)
            _require(obs["seq"] in (session.seq, session.seq + 1), "invalid_finish_sequence", 409)
            _require(obs["mission"] == session.mission and
                     (obs["width"], obs["height"]) == session.size, "session_scenario_changed", 409)
            session.agent.check_observation(obs)
            try:
                result = session.agent.finish(obs)
                response = json.dumps(result, ensure_ascii=True, allow_nan=False, separators=(",", ":")).encode("utf-8")
                session.recorder.close()
            except Exception:
                session.failed = True
                raise
            session.finish_fingerprint, session.finish_response = fingerprint, response
            session.closed = True
            return response

    def close(self):
        with self.lock:
            for session in self.sessions.values():
                session.recorder.close()
