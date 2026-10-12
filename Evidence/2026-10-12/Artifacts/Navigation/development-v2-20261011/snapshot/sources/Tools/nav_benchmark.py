"""Independent CPU navigation evaluator; never imported by the production actor.

python3 -B Tools/nav_benchmark.py --phase development
python3 -B Tools/nav_benchmark.py --phase confirmation --freeze <run>/freeze.json

The default launcher returns immediately; --worker runs in the foreground.
Development acquires a real checkpoint before the three matched comparisons.
Confirmation requires an unchanged source/dataset/model freeze and is never
selected implicitly. Only public observations cross the service boundary.
"""
import argparse
from collections import Counter
from concurrent.futures import ProcessPoolExecutor
from datetime import datetime, timezone
import hashlib
import json
import math
import multiprocessing
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
DEFAULT_WORLDS = ROOT / "Assets/StreamingAssets/Navigation/worlds-v2.json"
DEFAULT_MANIFEST = ROOT / "Artifacts/Navigation/worlds-v2.seed-manifest.json"
DECISION_LIMIT = 320
WORK_LIMIT = 2_000_000
PROFILES = ("trained", "cold", "no_memory")
OBSERVATION_KEYS = frozenset("schema session seq world profile width height x z goalX goalZ yaw completed rays outcome".split())
OUTCOME_KEYS = frozenset("present session seq action command status fromX fromZ x z distanceMm".split())
RAY_KEYS = frozenset("sx sz ex ez hit".split())


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()


def sha256_file(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(encoded(value) + b"\n")


def empty_outcome():
    return {"present":False,"session":"","seq":-1,"action":"","command":-1,
            "status":"","fromX":0,"fromZ":0,"x":0,"z":0,"distanceMm":0}


def assert_public_observation(obs):
    """Fail closed on accidental truth/semantic fields, including nested rays."""
    if type(obs) is not dict or set(obs) != OBSERVATION_KEYS or obs["schema"] != "echo.navigation.observation.v1":
        raise ValueError("non-public observation fields")
    if type(obs["outcome"]) is not dict or set(obs["outcome"]) != OUTCOME_KEYS:
        raise ValueError("non-public outcome fields")
    if type(obs["rays"]) is not list or len(obs["rays"]) > 65:
        raise ValueError("invalid ray array")
    for ray in obs["rays"]:
        if type(ray) is not dict or set(ray) != RAY_KEYS or type(ray["hit"]) is not bool:
            raise ValueError("non-public ray fields")
        if any(type(ray[key]) is not int for key in ("sx","sz","ex","ez")):
            raise ValueError("ray coordinates must be integer millimetres")
        if math.hypot(ray["ex"]-ray["sx"], ray["ez"]-ray["sz"]) > 12001:
            raise ValueError("ray outside contract range")
    return obs


def ray_box_distance(origin, direction, centre, *, maximum=10000, half_size=960):
    """Slab intersection of a normalised x/z ray with a 1.92 m cube in mm."""
    near, far = 0.0, float(maximum)
    for start, delta, middle in zip(origin, direction, centre):
        low, high = middle-half_size, middle+half_size
        if abs(delta) < 1e-12:
            if not low <= start <= high:
                return None
            continue
        entry, leave = (low-start)/delta, (high-start)/delta
        near, far = max(near, min(entry,leave)), min(far, max(entry,leave))
        if near > far:
            return None
    return near


class NavigationEnvironment:
    """Private geometry/actuators with a single public sensor/receipt interface."""
    def __init__(self, case):
        self.case = case
        self.width, self.height = case["width"], case["height"]
        self.position = case["start"]["x"], case["start"]["z"]
        self.goal = case["goal"]["x"], case["goal"]["z"]
        self._walls = frozenset((p["x"],p["z"]) for p in case["walls"])
        self._centres = tuple((x*2000,z*2000) for x,z in sorted(self._walls))
        self._mapping = tuple((p["x"],p["z"]) for p in case["mapping"])
        self.yaw = 0
        self.outcome = empty_outcome()

    @property
    def completed(self):
        return self.position == self.goal

    def capture_depth(self):
        sx, sz = (coordinate*2000 for coordinate in self.position)
        angle = math.radians(self.yaw)
        sine, cosine = math.sin(angle), math.cos(angle)
        scale = math.tan(math.radians(50))
        rays = []
        for index in range(61):
            # Match RobotSensor's centres of the outer pixels of its 384px image.
            u = (.5 + 383*index/60)/384
            lateral = (2*u-1)*scale
            norm = math.hypot(lateral,1)
            dx, dz = (sine+lateral*cosine)/norm, (cosine-lateral*sine)/norm
            nearest, hit = 10000.0, False
            for centre in self._centres:
                distance = ray_box_distance((sx,sz),(dx,dz),centre,maximum=nearest)
                if distance is not None:
                    nearest, hit = distance, True
            rays.append(dict(sx=sx,sz=sz,ex=round(sx+dx*nearest),ez=round(sz+dz*nearest),hit=hit))
        return rays

    def observation(self, session, seq, profile):
        return assert_public_observation({"schema":"echo.navigation.observation.v1",
            "session":session,"seq":seq,"world":self.case["id"],"profile":profile,
            "width":self.width,"height":self.height,"x":self.position[0],"z":self.position[1],
            "goalX":self.goal[0],"goalZ":self.goal[1],"yaw":self.yaw,"completed":self.completed,
            "rays":self.capture_depth(),"outcome":dict(self.outcome)})

    def apply(self, proposal):
        action = proposal["action"]
        if action in ("done", "wait"):
            return None
        if action not in ("move", "scan"):
            raise ValueError("invalid proposed action")
        start = self.position
        if action == "move":
            command = proposal["command"]
            if type(command) is not int or not 0 <= command < 4:
                raise ValueError("invalid opaque command")
            dx, dz = self._mapping[command]
            target = start[0]+dx, start[1]+dz
            blocked = target in self._walls or not(0 <= target[0] < self.width and 0 <= target[1] < self.height)
            if not blocked:
                self.position = target
            status, residual = ("blocked",2000) if blocked else ("arrived",0)
        else:
            if proposal["command"] != -1 or type(proposal["yaw"]) is not int or not 0 <= proposal["yaw"] < 360:
                raise ValueError("invalid scan")
            self.yaw = proposal["yaw"]
            status, residual = "scanned", 0
        self.outcome = dict(present=True,session=proposal["session"],seq=proposal["seq"],action=action,
                            command=proposal["command"],status=status,fromX=start[0],fromZ=start[1],
                            x=self.position[0],z=self.position[1],distanceMm=residual)
        return dict(self.outcome)


def core_api():
    """Read the installed core; never install packages or write frozen bytecode."""
    source = Path(os.environ.get("ECHO5_SOURCE", "/home/roger/REALRXOS/RXos/echoai/ECHO-5")).resolve()
    if not (source / "src/echo5/interaction.py").is_file():
        raise RuntimeError("ECHO5_SOURCE does not contain the installed core")
    if str(source / "src") not in sys.path:
        sys.path.insert(0, str(source / "src"))
    from echo5.trace import Recorder, verify_journal
    if Path(sys.modules[Recorder.__module__].__file__).resolve() != source / "src/echo5/trace.py":
        raise RuntimeError("a different core installation was already imported")
    # Resolve deferred imports before taking a source snapshot. No experiment runs.
    from echo5.legacy import module
    module("echo_arc.observe")
    module("nexus0.world")
    return Recorder, verify_journal


def navigation_api():
    # The evaluator depends on the actor; the actor must never depend on this file.
    core_api()
    if str(ROOT / "Bridge") not in sys.path:
        sys.path.insert(0, str(ROOT / "Bridge"))
    from navigation import NavigationService
    return NavigationService


def checked_decision(raw, observation, previous_work):
    decision = json.loads(raw)
    if decision.get("schema") != "echo.navigation.decision.v1" or any(decision.get(k) != observation[k] for k in ("session","seq")):
        raise ValueError("decision is not bound to the observation")
    if decision.get("action") not in ("move","scan","done","wait"):
        raise ValueError("unsupported decision")
    used, remaining = decision.get("budgetUsed"), decision.get("budgetRemaining")
    if type(used) is not int or type(remaining) is not int or not previous_work <= used <= WORK_LIMIT or used+remaining != WORK_LIMIT:
        raise ValueError("profiles must use the same monotone 2,000,000-unit budget")
    return decision


def journal_receipt(path):
    _, verify = core_api()
    return {"path":str(path),"sha256":sha256_file(path),**verify(path)}


def run_episode(service, case, profile, artifact_root, *, expected_model_hash=None):
    """Run one bounded episode, recording every request/proposal/actual receipt."""
    Recorder, _ = core_api()
    destination = Path(artifact_root)
    destination.mkdir(parents=True, exist_ok=True)
    transcript = destination / "transcript.jsonl"
    if transcript.exists():
        raise ValueError("refusing to reuse an episode transcript")
    env = NavigationEnvironment(case)
    session = uuid.uuid4().hex
    seq, used = 0, 0
    started = time.monotonic()
    agent_journals = set()
    with Recorder(transcript) as recorder:
        recorder.write(kind="navigation_evaluation_start",case=case["id"],profile=profile,
                       caseSha256=hashlib.sha256(encoded(case)).hexdigest(),session=session,
                       decisionLimit=DECISION_LIMIT,workLimit=WORK_LIMIT)
        while seq < DECISION_LIMIT:
            observation = env.observation(session,seq,profile)
            raw = encoded(observation)
            response = service.process(raw)
            proposal = checked_decision(response,observation,used)
            if profile in ("trained","no_memory","remembered") and expected_model_hash is not None and proposal["modelHash"] != expected_model_hash:
                raise ValueError("frozen profile changed or failed to restore its model")
            used = proposal["budgetUsed"]
            if proposal.get("journal"):
                agent_journals.add(proposal["journal"])
            before = {"x":env.position[0],"z":env.position[1]}
            outcome = env.apply(proposal)
            recorder.write(kind="navigation_evaluation_step",observation=observation,proposal=proposal,
                requestUtf8=raw.decode(),responseUtf8=response.decode(),requestSha256=hashlib.sha256(raw).hexdigest(),
                responseSha256=hashlib.sha256(response).hexdigest(),before=before,
                after={"x":env.position[0],"z":env.position[1]},goal={"x":env.goal[0],"z":env.goal[1]},
                outcome=outcome,completed=env.completed)
            if proposal["action"] in ("move","scan"):
                seq += 1
            if env.completed or proposal["action"] in ("done","wait"):
                break
        terminal = env.observation(session,seq,profile)
        finish_raw = encoded(terminal)
        finish_response = service.finish(finish_raw)
        finish = json.loads(finish_response)
        if (finish.get("schema") != "echo.navigation.finish.v1" or finish.get("status") != "closed"
                or finish.get("session") != session or finish.get("seq") != seq):
            raise ValueError("service did not close the episode")
        receipts = []
        for path in sorted(agent_journals):
            receipt = journal_receipt(path)
            receipts.append(receipt)
            # The final observation can charge acquisition work after the last proposal.
            for line in Path(path).read_text().splitlines():
                event = json.loads(line)
                budget = event.get("budget")
                if isinstance(budget,dict) and "used" in budget:
                    if budget.get("limit") != WORK_LIMIT or not 0 <= budget["used"] <= WORK_LIMIT:
                        raise ValueError("agent journal violates the common work budget")
                    used = max(used,budget["used"])
        recorder.write(kind="navigation_evaluation_finish",observation=terminal,acknowledgement=finish,
                       requestUtf8=finish_raw.decode(),responseUtf8=finish_response.decode(),
                       work=used,agentJournals=receipts,elapsedSeconds=time.monotonic()-started)
    audit = audit_transcript(transcript,case)
    result = dict(audit["metrics"],case=case["id"],family=case["family"],split=case["split"],profile=profile,
                  transcript=str(transcript),transcriptSha256=sha256_file(transcript),audit={k:v for k,v in audit.items() if k!="metrics"},
                  elapsedSeconds=time.monotonic()-started)
    result["restorationVerified"] = (profile in ("trained","no_memory","remembered") and
        bool(expected_model_hash) and result["initialModelHash"] == expected_model_hash and
        result["initialLearnedActions"] == 4 and all(value >= 2 for value in result["initialSupport"]))
    save_json(destination / "result.json",result)
    return result


def audit_transcript(path, case):
    """Replay all physical outcomes and rays; explanation text never earns credit."""
    _, verify = core_api()
    chain = verify(path)
    rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    if len(rows) < 3 or rows[0]["kind"] != "navigation_evaluation_start" or rows[-1]["kind"] != "navigation_evaluation_finish":
        raise ValueError("incomplete episode transcript")
    header, terminal = rows[0], rows[-1]
    if (header["case"] != case["id"] or header["caseSha256"] != hashlib.sha256(encoded(case)).hexdigest()
            or header["decisionLimit"] != DECISION_LIMIT or header["workLimit"] != WORK_LIMIT):
        raise ValueError("changed case or budget")
    env = NavigationEnvironment(case)
    counts = Counter(moves=0,scans=0,actualSteps=0,failures=0,repeatedMoves=0,repeatedFailures=0)
    attempts, failures, used = set(), set(), 0
    steps = rows[1:-1]
    if not 1 <= len(steps) <= DECISION_LIMIT:
        raise ValueError("decision limit violated")
    for seq,row in enumerate(steps):
        if row["kind"] != "navigation_evaluation_step":
            raise ValueError("unexpected transcript event")
        expected = env.observation(header["session"],seq,header["profile"])
        assert_public_observation(row["observation"])
        if row["observation"] != expected or json.loads(row["requestUtf8"]) != expected:
            raise ValueError("observation does not match actual pose/rays/receipt")
        raw, response = row["requestUtf8"].encode(), row["responseUtf8"].encode()
        if hashlib.sha256(raw).hexdigest() != row["requestSha256"] or hashlib.sha256(response).hexdigest() != row["responseSha256"]:
            raise ValueError("raw transaction hash mismatch")
        proposal = checked_decision(response,expected,used)
        if proposal != row["proposal"]:
            raise ValueError("proposal differs from raw reply")
        if header["profile"] in ("trained","no_memory","remembered"):
            first = steps[0]["proposal"]
            if proposal["modelHash"] != first["modelHash"] or proposal["support"] != first["support"]:
                raise ValueError("frozen model changed within the episode")
        used = proposal["budgetUsed"]
        before = env.position
        outcome = env.apply(proposal)
        if (row["before"] != dict(x=before[0],z=before[1]) or row["after"] != dict(x=env.position[0],z=env.position[1])
            or row["goal"] != dict(x=env.goal[0],z=env.goal[1]) or row["outcome"] != outcome or row["completed"] != env.completed):
            raise ValueError("forged physical result")
        action = proposal["action"]
        if action == "move":
            counts["moves"] += 1
            attempt = before, proposal["command"]
            counts["repeatedMoves"] += attempt in attempts
            attempts.add(attempt)
            counts["actualSteps"] += env.position != before
            if outcome["status"] == "blocked":
                counts["failures"] += 1
                counts["repeatedFailures"] += attempt in failures
                failures.add(attempt)
        elif action == "scan":
            counts["scans"] += 1
        if seq < len(steps)-1 and (env.completed or action in ("done","wait")):
            raise ValueError("actions after episode termination")
    terminal_seq = len(steps)-1 + int(steps[-1]["proposal"]["action"] in ("move","scan"))
    expected_terminal = env.observation(header["session"],terminal_seq,header["profile"])
    if terminal["observation"] != expected_terminal or json.loads(terminal["requestUtf8"]) != expected_terminal:
        raise ValueError("terminal observation mismatch")
    acknowledgement = json.loads(terminal["responseUtf8"])
    if (acknowledgement != terminal["acknowledgement"] or acknowledgement.get("schema") != "echo.navigation.finish.v1"
            or acknowledgement.get("status") != "closed" or acknowledgement.get("session") != header["session"]
            or acknowledgement.get("seq") != terminal_seq):
        raise ValueError("missing actual finish acknowledgement")
    if header["profile"] in ("trained","no_memory","remembered"):
        first = steps[0]["proposal"]
        if (acknowledgement.get("modelHash",first["modelHash"]) != first["modelHash"]
                or acknowledgement.get("support",first["support"]) != first["support"]):
            raise ValueError("terminal ingestion changed the frozen model")
    measured_work = used
    map_restored = False
    for receipt in terminal["agentJournals"]:
        if journal_receipt(receipt["path"]) != receipt:
            raise ValueError("agent receipt changed")
        for line in Path(receipt["path"]).read_text().splitlines():
            event = json.loads(line)
            if event["kind"]=="navigation_map_restore":
                map_restored = True
            budget = event.get("budget")
            if isinstance(budget,dict) and "used" in budget:
                if budget.get("limit") != WORK_LIMIT or not 0 <= budget["used"] <= WORK_LIMIT:
                    raise ValueError("agent journal violates the common work budget")
                measured_work = max(measured_work,budget["used"])
    if terminal["work"] != measured_work or not used <= measured_work <= WORK_LIMIT:
        raise ValueError("terminal work disagrees with recorded actual work")
    if "budgetUsed" in acknowledgement and (acknowledgement["budgetUsed"] != measured_work
            or acknowledgement["budgetUsed"]+acknowledgement["budgetRemaining"] != WORK_LIMIT):
        raise ValueError("terminal acknowledgement work mismatch")
    first, last = steps[0]["proposal"], steps[-1]["proposal"]
    metrics = dict(counts,success=env.completed,decisions=len(steps),actions=counts["moves"]+counts["scans"],
                   work=terminal["work"],decisionLimit=DECISION_LIMIT,workLimit=WORK_LIMIT,
                   finalPosition=dict(x=env.position[0],z=env.position[1]),goal=dict(x=env.goal[0],z=env.goal[1]),
                   initialModelHash=first["modelHash"],finalModelHash=acknowledgement.get("modelHash",last["modelHash"]),
                   initialLearnedActions=first["learnedActions"],initialSupport=first["support"],
                   learnedActions=last["learnedActions"],support=acknowledgement.get("support",last["support"]),
                   mapRestored=map_restored,agentJournals=terminal["agentJournals"])
    return {"valid":True,"events":chain["events"],"lastHash":chain["last_hash"],"metrics":metrics}


def summarise(rows):
    return {"episodes":len(rows),"successes":sum(row["success"] for row in rows),
            **{key:sum(row.get(key,0) for row in rows) for key in
               ("decisions","actions","moves","scans","actualSteps","failures","repeatedMoves","repeatedFailures","work")}}


def classify_transfer(rows, phase, acquisition):
    arms = {profile:[row for row in rows if row["profile"]==profile] for profile in PROFILES}
    summaries = {profile:summarise(episodes) for profile,episodes in arms.items()}
    trained = {r["case"]:r for r in arms["trained"]}
    cold = {r["case"]:r for r in arms["cold"]}
    paired = sorted(key for key in trained.keys() & cold.keys() if trained[key]["success"] and cold[key]["success"])
    gain = sum(cold[key]["actions"]-trained[key]["actions"] for key in paired)
    steps_gain = sum(cold[key]["actualSteps"]-trained[key]["actualSteps"] for key in paired)
    checks = {
        "confirmationPhase":phase=="confirmation",
        "sixteenMatchedCases":len(trained)==16 and all(len(arms[p])==16 and {r["case"] for r in arms[p]}==set(trained) for p in PROFILES),
        "atLeastThirteenGoals":summaries["trained"]["successes"]>=13,
        "reproducibleAcquisition":bool(acquisition.get("reproducible")),
        "allTranscriptsAudited":bool(rows) and all(row.get("audit",{}).get("valid") for row in rows),
        "checkpointRestored":all(row.get("restorationVerified") for p in ("trained","no_memory") for row in arms[p]),
        "successRatePreserved":summaries["trained"]["successes"]>=summaries["cold"]["successes"],
        "fewerActionsOnPairedSuccesses":bool(paired) and gain>0,
        "fewerStepsOnPairedSuccesses":bool(paired) and steps_gain>0,
        "equalBudgets":all(row["decisions"]<=DECISION_LIMIT and row["work"]<=WORK_LIMIT for row in rows),
    }
    valid = all(checks.values())
    return {"validTransfer":valid,"classification":("valid_transfer" if valid else
            "development_only" if phase=="development" else "gate_not_met"),"checks":checks,"profiles":summaries,
            "pairedSuccessfulCases":len(paired),"pairedActionGain":gain,"pairedStepGain":steps_gain,
            "note":"CPU navigation only; acquisition cost excluded from evaluation totals; no-memory control reported without assumed inferiority."}


def snapshot_files(files, destination):
    destination = Path(destination)
    records = {}
    for name, original in sorted(files.items()):
        relative = Path(name)
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError("unsafe snapshot path")
        original = Path(original).resolve()
        target = destination / relative
        target.parent.mkdir(parents=True,exist_ok=True)
        payload = original.read_bytes()
        with target.open("xb") as stream:
            stream.write(payload)
        records[name] = {"original":str(original),"sha256":hashlib.sha256(payload).hexdigest()}
    return records


def verify_snapshot(records, destination, *, check_current):
    for name, record in records.items():
        relative = Path(name)
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError("unsafe snapshot path")
        if sha256_file(Path(destination)/relative) != record["sha256"]:
            raise ValueError("changed frozen snapshot: " + name)
        if check_current and sha256_file(record["original"]) != record["sha256"]:
            raise ValueError("changed frozen source/data/model: " + name)


def source_files():
    files = {}
    for folder,pattern in (("Bridge","*.py"),("Assets/Echo","*.cs")):
        for path in (ROOT/folder).rglob(pattern):
            if "tests" not in path.parts:
                files[str(path.relative_to(ROOT))] = path
    for name in ("DESIGN.md","PROTOCOL.md"):
        files["Docs/Navigation/"+name] = ROOT/"Docs/Navigation"/name
    for name in ("make_navigation_worlds.py","nav_benchmark.py"):
        files["Tools/"+name] = ROOT/"Tools"/name
    core = Path(os.environ.get("ECHO5_SOURCE", "/home/roger/REALRXOS/RXos/echoai/ECHO-5")).resolve()
    for path in (core/"src/echo5").rglob("*.py"):
        files["core/"+str(path.relative_to(core))] = path
    # Include the exact inherited dependencies loaded by HypothesisAgent/Recorder.
    for module in list(sys.modules.values()):
        filename = getattr(module,"__file__",None)
        if filename:
            path = Path(filename).resolve()
            if path.suffix == ".py" and path.is_relative_to(core.parent) and not path.is_relative_to(core):
                files["inherited/"+str(path.relative_to(core.parent))] = path
            elif path.suffix == ".py" and path.parent == core.parent.parent:
                files["inherited-rxos/"+path.name] = path
    return files


def source_hash(files):
    return hashlib.sha256(encoded({name:sha256_file(path) for name,path in sorted(files.items())})).hexdigest()


def load_phase(worlds, manifest_path, phase):
    """Access selected geometry only; confirmation is gated by the caller first."""
    manifest = json.loads(Path(manifest_path).read_bytes())
    raw = Path(worlds).read_bytes()
    if manifest["datasetSha256"] != hashlib.sha256(raw).hexdigest():
        raise ValueError("dataset differs from its separate seed manifest")
    data = json.loads(raw)
    if data.get("schema") != "echo.navigation.worlds.v1":
        raise ValueError("invalid dataset schema")
    counts = Counter(case["split"] for case in data["cases"])
    if counts != {"acquisition":4,"development":8,"confirmation":16} or dict(counts) != manifest["counts"]:
        raise ValueError("incorrect preregistered split counts")
    records = {record["id"]:record for record in manifest["cases"]}
    if len(records)!=28 or len({case["id"] for case in data["cases"]})!=28:
        raise ValueError("duplicate or missing case identities")
    selected = [case for case in data["cases"] if case["split"]==phase or (phase=="development" and case["split"]=="acquisition")]
    from Tools.make_navigation_worlds import validate_case
    for case in selected:
        validate_case(case)
        if records[case["id"]]["caseSha256"] != hashlib.sha256(encoded(case)+b"\n").hexdigest():
            raise ValueError("case differs from the seed manifest")
    return selected,manifest


def run_comparison_task(task):
    case,profile,root,model,expected_hash = task
    destination = Path(root)/case["id"]/profile
    state = destination/"state"
    state.mkdir(parents=True,exist_ok=True)
    local_model = state/"motor.json"
    if profile != "cold":
        shutil.copyfile(model,local_model)
    before = sha256_file(local_model) if local_model.exists() else None
    service = navigation_api()(artifact_root=state/"agent",model_path=local_model)
    try:
        result = run_episode(service,case,profile,destination,expected_model_hash=expected_hash)
    finally:
        service.close()
    after = sha256_file(local_model) if local_model.exists() else None
    if before != after:
        raise ValueError("evaluation profile changed the frozen model")
    return result


def acquired_model_receipt(path, episodes):
    """Verify a checkpoint by restoring actual core evidence, never synthesize it."""
    from echo5.budget import Budget
    from echo5.interaction import HypothesisAgent
    saved = json.loads(Path(path).read_bytes())
    budget = Budget(WORK_LIMIT)
    restored = HypothesisAgent.restore(saved["checkpoint"],budget=budget)
    exact = restored.checkpoint() == saved["checkpoint"]
    support = saved["checkpoint"]["payload"]["model"]["support"]
    hypotheses = saved["checkpoint"]["payload"]["model"]["hypotheses"]
    commands = Counter()
    distinct = set()
    for episode in episodes:
        for line in Path(episode["transcript"]).read_text().splitlines():
            row = json.loads(line)
            outcome = row.get("outcome")
            if outcome and outcome["action"]=="move" and outcome["status"]=="arrived":
                commands[str(outcome["command"])] += 1
                distinct.add((outcome["command"],outcome["fromX"],outcome["fromZ"],outcome["x"],outcome["z"]))
    matched_projections = True
    projection_signatures = set()
    for record in saved["checkpoint"]["payload"]["evidence"]:
        before,after = record["before"],record["after"]
        signature = (record["action"],before["position"][1],before["position"][0],after["position"][1],after["position"][0])
        expected = dict(schema="echo5.interaction.observation.v1",width=16,height=16,walls=[],
                        position=before["position"],goal=[0,0],actions=[0,1,2,3],tick=0)
        matched_projections &= (signature in distinct and signature not in projection_signatures and before==expected
                                and after==dict(expected,position=after["position"],tick=1))
        projection_signatures.add(signature)
    reproducible = (exact and matched_projections and bool(saved.get("acquisition")) and len(support)==4 and all(x>=2 for x in support)
                    and len(hypotheses)==4 and all(len(h)==1 for h in hypotheses)
                    and all(str(i) in commands for i in range(4)) and all(row["audit"]["valid"] for row in episodes)
                    and episodes[0]["initialSupport"]==[0,0,0,0])
    return {"reproducible":reproducible,"checkpointRestoredExactly":exact,"checkpointSha256":sha256_file(path),
            "modelHash":saved["checkpoint"]["sha256"],"support":support,"singletonHypotheses":sum(len(h)==1 for h in hypotheses),
            "allProjectionsMatchPhysicalReceipts":bool(matched_projections),"confirmedMovesByCommand":dict(commands),
            "distinctPhysicalTransitions":len(distinct),"checkpointVerificationWork":budget.used,
            "counts":summarise(episodes),"episodes":episodes}


def memory_supplement(case,root,model,expected_hash):
    """Predeclared first case, trained then remembered, with the same map store."""
    destination = Path(root)/"memory-revisit"
    destination.mkdir(parents=True,exist_ok=True)
    local_model = destination/"motor.json"
    shutil.copyfile(model,local_model)
    before = sha256_file(local_model)
    episodes = []
    for profile in ("trained","remembered"):
        service = navigation_api()(artifact_root=destination/"agent",model_path=local_model)
        try:
            episodes.append(run_episode(service,case,profile,destination/profile,expected_model_hash=expected_hash))
        finally:
            service.close()
    if sha256_file(local_model) != before:
        raise ValueError("memory revisit changed the frozen actuator model")
    return {"case":case["id"],"selection":"first preregistered selected case", "episodes":episodes,
            "mapRestorationVerified":episodes[1]["mapRestored"],
            "actionGain":episodes[0]["actions"]-episodes[1]["actions"],
            "stepGain":episodes[0]["actualSteps"]-episodes[1]["actualSteps"],
            "note":"Same-world memory supplement; excluded from transfer counts."}


def verify_freeze(path, worlds, seed_manifest):
    path = Path(path).resolve()
    freeze = json.loads(path.read_bytes())
    if freeze.get("schema") != "echo.navigation.freeze.v1" or not freeze.get("acquisition",{}).get("reproducible"):
        raise ValueError("confirmation requires a completed acquisition/source freeze")
    base = path.parent
    verify_snapshot(freeze["sources"],base/"snapshot/sources",check_current=True)
    verify_snapshot(freeze["inputs"],base/"snapshot/inputs",check_current=False)
    if source_hash(source_files()) != freeze["sourceHash"]:
        raise ValueError("source set changed since freeze")
    if sha256_file(worlds) != freeze["inputs"]["worlds.json"]["sha256"] or sha256_file(seed_manifest) != freeze["inputs"]["seed-manifest.json"]["sha256"]:
        raise ValueError("dataset/seed manifest changed since freeze")
    for receipt in freeze["acquisitionReceipts"]:
        if sha256_file(base/receipt["path"]) != receipt["sha256"]:
            raise ValueError("acquisition receipt changed since freeze")
    return freeze,base/"snapshot/inputs/motor.json"


def progress(**event):
    print(json.dumps(event,sort_keys=True),flush=True)


def run_campaign(args):
    navigation_api()  # Fail before creating a run if the separately owned service is missing.
    root = args.output.resolve()
    root.mkdir(parents=True,exist_ok=False)
    try:
        freeze_input, model = (verify_freeze(args.freeze,args.worlds,args.manifest) if args.phase=="confirmation" else (None,None))
        selected, seed_manifest = load_phase(args.worlds,args.manifest,args.phase)
        sources = source_files()
        initial_source_hash = source_hash(sources)
        snapshots = snapshot_files(sources,root/"snapshot/sources")
        inputs = snapshot_files({"worlds.json":args.worlds,"seed-manifest.json":args.manifest},root/"snapshot/inputs")
        save_json(root/"manifest.json",{"schema":"echo.navigation.run.v1","phase":args.phase,
            "sourceHash":initial_source_hash,"sources":snapshots,"inputs":inputs,"workers":args.workers,
            "decisionLimit":DECISION_LIMIT,"workLimit":WORK_LIMIT,"profiles":PROFILES,
            "cases":[case["id"] for case in selected],"confirmationOpened":args.phase=="confirmation",
            "freezeSha256":sha256_file(args.freeze) if args.freeze else None})
        if args.phase=="development":
            training = root/"acquisition"
            model = training/"motor.json"
            acquisition_rows = []
            for case in [case for case in selected if case["split"]=="acquisition"]:
                service = navigation_api()(artifact_root=training/"agent",model_path=model)
                try:
                    row = run_episode(service,case,"learn",training/case["id"])
                finally:
                    service.close()
                acquisition_rows.append(row)
                progress(phase="acquisition",case=row["case"],success=row["success"],actions=row["actions"],work=row["work"])
            if not model.is_file():
                raise RuntimeError("acquisition did not produce a checkpoint; no comparison may use invented weights")
            acquisition = acquired_model_receipt(model,acquisition_rows)
            save_json(root/"acquisition.json",acquisition)
            if not acquisition["reproducible"]:
                raise RuntimeError("acquisition/restoration evidence is incomplete; see acquisition.json")
        else:
            acquisition = freeze_input["acquisition"]
            save_json(root/"acquisition-reference.json",{"freeze":str(args.freeze),"acquisition":acquisition})
        inputs.update(snapshot_files({"motor.json":model},root/"snapshot/inputs"))
        frozen_model = root/"snapshot/inputs/motor.json"
        cases = [case for case in selected if case["split"]==args.phase]
        tasks = [(case,profile,str(root/"episodes"),str(frozen_model),acquisition["modelHash"]) for case in cases for profile in PROFILES]
        rows = []
        with ProcessPoolExecutor(max_workers=args.workers,mp_context=multiprocessing.get_context("spawn")) as pool:
            for row in pool.map(run_comparison_task,tasks):
                rows.append(row)
                progress(phase=args.phase,profile=row["profile"],case=row["case"],success=row["success"],
                         actions=row["actions"],actualSteps=row["actualSteps"],work=row["work"])
        memory = memory_supplement(cases[0],root,frozen_model,acquisition["modelHash"])
        stable = source_hash(source_files()) == initial_source_hash
        classification = classify_transfer(rows,args.phase,acquisition)
        classification["checks"]["frozenSourcesUnchanged"] = stable
        if not stable:
            classification.update(validTransfer=False,classification="source_changed_during_run")
        report = {"schema":"echo.navigation.report.v1","phase":args.phase,"sourceHash":initial_source_hash,
                  "acquisition":acquisition,"comparison":classification,"episodes":rows,"memoryRevisit":memory,
                  "confirmationOpened":args.phase=="confirmation"}
        save_json(root/"report.json",report)
        if args.phase=="development" and stable:
            receipts = [{"path":"acquisition.json","sha256":sha256_file(root/"acquisition.json")}]
            for row in acquisition["episodes"]:
                for path in [Path(row["transcript"]),*[Path(r["path"]) for r in row["agentJournals"]]]:
                    receipts.append({"path":str(path.relative_to(root)),"sha256":sha256_file(path)})
            save_json(root/"freeze.json",{"schema":"echo.navigation.freeze.v1","sourceHash":initial_source_hash,
                "sources":snapshots,"inputs":inputs,"acquisition":acquisition,"acquisitionReceipts":receipts,
                "protocol":"320 decisions / 2000000 logical units; trained,cold,no_memory; first-case memory revisit",
                "confirmationCount":16,"confirmationOpened":False})
        progress(status="complete",report=str(root/"report.json"),comparison=classification,
                 freeze=str(root/"freeze.json") if (root/"freeze.json").exists() else None)
        return report
    except Exception as exc:
        save_json(root/"failure.json",{"status":"failed","error":type(exc).__name__,"message":str(exc),"phase":args.phase})
        raise


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--phase",choices=("development","confirmation"),default="development")
    parser.add_argument("--worlds",type=Path,default=DEFAULT_WORLDS)
    parser.add_argument("--manifest",type=Path,default=DEFAULT_MANIFEST)
    parser.add_argument("--freeze",type=Path)
    parser.add_argument("--output",type=Path)
    parser.add_argument("--workers",type=int,choices=(1,2),default=2)
    parser.add_argument("--worker",action="store_true",help="run foreground worker (default launches background subprocess)")
    args = parser.parse_args(argv)
    if args.phase=="confirmation" and args.freeze is None:
        parser.error("confirmation requires explicit --phase confirmation --freeze <development-run>/freeze.json")
    if args.phase=="development" and args.freeze is not None:
        parser.error("development always acquires a fresh model; --freeze is for confirmation only")
    if args.output is None:
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        args.output = ROOT/"Artifacts/Navigation"/(args.phase+"-"+stamp+"-"+uuid.uuid4().hex[:8])
    if not args.output.resolve().is_relative_to(ROOT):
        parser.error("benchmark artifacts must stay within ECHO-Station")
    return args


def limit_resources():
    # Inherited by spawned episode workers; at most two cores, low priority.
    if hasattr(os,"nice"):
        os.nice(10)
    if hasattr(os,"sched_getaffinity"):
        os.sched_setaffinity(0,set(sorted(os.sched_getaffinity(0))[:2]))
    for name in ("OMP_NUM_THREADS","OPENBLAS_NUM_THREADS","MKL_NUM_THREADS","NUMEXPR_NUM_THREADS"):
        os.environ[name] = "1"


def main(argv=None):
    args = parse_args(argv)
    navigation_api()
    if args.phase=="confirmation":
        verify_freeze(args.freeze,args.worlds,args.manifest)
    if args.worker:
        limit_resources()
        run_campaign(args)
        return
    if args.output.exists():
        raise ValueError("output directory already exists; use a fresh run directory")
    args.output.parent.mkdir(parents=True,exist_ok=True)
    log = args.output.with_suffix(".log")
    command = [sys.executable,"-B","-u",str(Path(__file__).resolve()),"--worker","--phase",args.phase,
               "--workers",str(args.workers),"--worlds",str(args.worlds.resolve()),
               "--manifest",str(args.manifest.resolve()),"--output",str(args.output.resolve())]
    if args.freeze:
        command.extend(["--freeze",str(args.freeze.resolve())])
    with log.open("xb") as stream:
        process = subprocess.Popen(command,cwd=ROOT,stdin=subprocess.DEVNULL,stdout=stream,stderr=subprocess.STDOUT,
                                   start_new_session=True,env=dict(os.environ,PYTHONDONTWRITEBYTECODE="1"))
    receipt = {"schema":"echo.navigation.launch.v1","phase":args.phase,"pid":process.pid,
               "output":str(args.output.resolve()),"log":str(log),"command":command,"workers":args.workers}
    save_json(args.output.with_suffix(".launch.json"),receipt)
    progress(**receipt)


if __name__ == "__main__":
    main()
