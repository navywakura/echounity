"""Depth/odometry adapter to the installed ECHO-5 learner and planner.

No evaluator, command mapping, effect prior, RGB semantics, or world generator
is available here. Geometric sensing/frontier selection are declared algorithms.
"""

from collections import Counter, deque
from dataclasses import dataclass
from fractions import Fraction
import hashlib
import json
import os
from pathlib import Path
import threading
import uuid

# This existing import boundary disables bytecode and verifies ECHO5_SOURCE.
from station_agent import ProtocolError, MAX_BODY, _fields, _integer, _text, _require
from echo5.budget import Budget, BudgetExceeded
from echo5.interaction import HypothesisAgent
from echo5.trace import Recorder


DEFAULT_ARTIFACTS = Path(__file__).resolve().parents[1] / "Artifacts/Navigation"
WORK_LIMIT = 2_000_000
DECISION_LIMIT = 320
SESSION_LIMIT = 16
CLOSED_CACHE_LIMIT = 64
PROFILES = ("learn", "trained", "cold", "no_memory", "remembered")
OBS_KEYS = "schema session seq world profile width height x z goalX goalZ yaw completed rays outcome".split()
OUTCOME_KEYS = "present session seq action command status fromX fromZ x z distanceMm".split()
RAY_KEYS = "sx sz ex ez hit".split()


def _bytes(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=True, allow_nan=False,
                      separators=(",", ":")).encode("utf-8")


def _json(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            _require(key not in result, "duplicate_json_key")
            result[key] = value
        return result

    def constant(_):
        raise ProtocolError(400, "nonfinite_json_number")

    try:
        return json.loads(raw.decode("utf-8"), object_pairs_hook=pairs, parse_constant=constant)
    except (UnicodeError, ValueError, TypeError, RecursionError) as exc:
        if isinstance(exc, ProtocolError):
            raise
        raise ProtocolError(400, "invalid_json") from exc


def validate_observation(obs):
    """Exact, bounded public schema; bool is never accepted as an integer."""
    _fields(obs, OBS_KEYS)
    _require(obs["schema"] == "echo.navigation.observation.v1", "unsupported_schema")
    for name in ("session", "world"):
        _text(obs[name], 128, identifier=True)
    _integer(obs["seq"], 0, 2**31 - 1)
    _require(type(obs["profile"]) is str and obs["profile"] in PROFILES)
    for name in ("width", "height"):
        _integer(obs[name], 5, 16)
    for name in ("x", "goalX"):
        _integer(obs[name], 0, obs["width"] - 1)
    for name in ("z", "goalZ"):
        _integer(obs[name], 0, obs["height"] - 1)
    _integer(obs["yaw"], 0, 359)
    _require(type(obs["completed"]) is bool)
    _require(not obs["completed"] or _point(obs) == (obs["goalX"], obs["goalZ"]),
             "completion_pose_mismatch")
    _require(type(obs["rays"]) is list and len(obs["rays"]) <= 65)
    for ray in obs["rays"]:
        _fields(ray, RAY_KEYS)
        _require(type(ray["hit"]) is bool)
        for axis, size in (("x", obs["width"]), ("z", obs["height"])):
            # World coordinates, including sub-cell camera offsets and endpoints
            # outside the board. Never rotate these rays again by camera yaw.
            _integer(ray["s" + axis], -1000, size * 2000 - 1000)
            _integer(ray["e" + axis], -13000, size * 2000 + 11000)
        _require((ray["ex"] - ray["sx"])**2 + (ray["ez"] - ray["sz"])**2 <= 12000**2,
                 "ray_range_exceeded")
    outcome = obs["outcome"]
    _fields(outcome, OUTCOME_KEYS)
    _require(type(outcome["present"]) is bool)
    for name in ("session", "action", "status"):
        _text(outcome[name], 128)
    _integer(outcome["seq"], -1, 2**31 - 1)
    _integer(outcome["command"], -1, 3)
    for name in ("fromX", "x"):
        _integer(outcome[name], 0, obs["width"] - 1)
    for name in ("fromZ", "z"):
        _integer(outcome[name], 0, obs["height"] - 1)
    _integer(outcome["distanceMm"], 0, 2**31 - 1)
    if not outcome["present"]:
        _require(outcome["seq"] == -1 and outcome["command"] == -1
                 and all(outcome[k] == "" for k in ("session", "action", "status"))
                 and all(outcome[k] == 0 for k in ("fromX", "fromZ", "x", "z", "distanceMm")),
                 "invalid_empty_outcome")
    else:
        _text(outcome["session"], 128, identifier=True)
        _integer(outcome["seq"], 0, 2**31 - 1)
        if outcome["action"] == "move":
            _integer(outcome["command"], 0, 3)
            _require(outcome["status"] in ("arrived", "blocked", "cancelled"), "invalid_move_status")
        else:
            _require(outcome["action"] == "scan" and outcome["command"] == -1
                     and outcome["status"] in ("scanned", "cancelled")
                     and outcome["distanceMm"] == 0, "invalid_scan_outcome")
    return obs


def decode_observation(raw):
    _require(type(raw) is bytes)
    _require(len(raw) <= MAX_BODY, "body_too_large", 413)
    return validate_observation(_json(raw))


def _point(value):
    return value["x"], value["z"]


def _xy(point):
    return dict(x=point[0], z=point[1])


def _distance(a, b):
    return abs(a[0] - b[0]) + abs(a[1] - b[1])


def _inside(point, width, height):
    return 0 <= point[0] < width and 0 <= point[1] < height


def _neighbors(point):
    x, z = point
    # Geometric neighbors only. These are NOT indexed by command handles.
    return ((x, z + 1), (x + 1, z), (x, z - 1), (x - 1, z))


def _ray_cells(ray, width, height):
    """Measured occupancy with conservative free-space support at cell centres.

    Entering a cell's outer margin does not establish navigable space there.
    Require the traversed segment to cross its central 1m square (the grid is
    2m). This is a geometric observation rule, independent of hidden colliders.
    """
    sx, sz, ex, ez = (ray[k] for k in ("sx", "sz", "ex", "ez"))
    dx, dz = ex - sx, ez - sz
    cuts = {Fraction(0), Fraction(1)}
    for start, delta, size in ((sx, dx, width), (sz, dz, height)):
        if delta:
            for index in range(size + 1):
                t = Fraction(index * 2000 - 1000 - start, delta)
                if 0 < t < 1:
                    cuts.add(t)
    hit = None
    if ray["hit"]:
        # At a face, the obstacle is on the far side of the incoming ray.
        hx = Fraction(ex) + Fraction((dx > 0) - (dx < 0), 10**9)
        hz = Fraction(ez) + Fraction((dz > 0) - (dz < 0), 10**9)
        candidate = (int((hx + 1000) // 2000), int((hz + 1000) // 2000))
        if _inside(candidate, width, height):
            hit = candidate
    free = set()
    on_face = (dx == 0 and (sx + 1000) % 2000 == 0) or (dz == 0 and (sz + 1000) % 2000 == 0)
    if (dx or dz) and not on_face:
        ordered = sorted(cuts)
        for a, b in zip(ordered, ordered[1:]):
            middle = (a + b) / 2
            cell = (int((sx + middle * dx + 1000) // 2000),
                    int((sz + middle * dz + 1000) // 2000))
            if not _inside(cell, width, height) or cell == hit:
                continue
            enter, leave = a, b
            for start, delta, index in ((sx, dx, cell[0]), (sz, dz, cell[1])):
                lo, hi = index * 2000 - 500, index * 2000 + 500
                if delta == 0:
                    if not lo < start < hi:
                        leave = enter
                        break
                else:
                    u, v = Fraction(lo - start, delta), Fraction(hi - start, delta)
                    enter, leave = max(enter, min(u, v)), min(leave, max(u, v))
            if enter < leave:
                free.add(cell)
    return free, hit


def _projection(origin, destination):
    # One independent physical sample. The goal is an invariant placeholder;
    # changing it per sample would launder duplicates into independent support.
    before = dict(schema="echo5.interaction.observation.v1", width=16, height=16,
                  walls=[], position=[origin[1], origin[0]], goal=[0, 0],
                  actions=[0, 1, 2, 3], tick=0)
    after = dict(before, position=[destination[1], destination[0]], tick=1)
    return before, after


def _signature(origin, command, destination):
    return origin, command, destination


def _matched(obs, previous, before):
    if previous is None or previous["action"] not in ("move", "scan"):
        return False
    receipt = obs["outcome"]
    expected = dict(session=previous["session"], seq=previous["seq"], action=previous["action"],
                    command=previous["command"], fromX=before["x"], fromZ=before["z"],
                    x=obs["x"], z=obs["z"])
    if not receipt["present"] or any(receipt[k] != v for k, v in expected.items()):
        return False
    if previous["action"] == "scan":
        return (_point(obs) == _point(before)
                and (receipt["status"] == "cancelled" or obs["yaw"] == previous["yaw"]))
    return True


class NavigationAgent:
    """Public observations in, actual core learning/planning and factual traces out."""

    def __init__(self, *, core, budget, recorder, journal_path):
        self.core, self.budget, self.recorder = core, budget, recorder
        self.journal_path = str(journal_path)
        self.cells = {}  # measured occupancy only
        self.blocked_edges = {}  # (origin, opaque command) -> inference, never core evidence
        self.scanned = {}
        self.visits = Counter()
        self.attempts = Counter()
        self.failed_edges = set()
        self.last_observation = self.last_decision = None
        self.valid = True
        self.stopped = ""
        self.calibrations = []
        self.metrics = Counter()
        self._refresh_model()

    def _refresh_model(self):
        self.checkpoint = self.core.checkpoint()
        self.model = self.checkpoint["payload"]["model"]
        self.seen_projections = {
            _signature((r["before"]["position"][1], r["before"]["position"][0]), r["action"],
                       (r["after"]["position"][1], r["after"]["position"][0]))
            for r in self.checkpoint["payload"]["evidence"]}

    def _effect(self, command):
        candidates = self.model["hypotheses"][command]
        return candidates[0] if len(candidates) == 1 and self.model["support"][command] else None

    def _target(self, origin, command):
        effect = self._effect(command)
        return None if effect is None else (origin[0] + effect[1], origin[1] + effect[0])

    def _inferred(self):
        return {tuple(edge["target"]) for edge in self.blocked_edges.values()
                if edge["target"] is not None and edge["infer_cell"]}

    def _remember(self, obs):
        self.budget.consume("navigation_sensor", 1 + len(obs["rays"]) * (obs["width"] + obs["height"] + 4))
        if obs["profile"] == "no_memory":
            self.cells.clear()
            self.blocked_edges.clear()
            # Retain only the current rotation sequence, not remote scan sites.
            self.scanned = {_point(obs): self.scanned.get(_point(obs), set())}
            self.visits.clear()
        free, occupied, signatures = set(), set(), {}
        for ray in obs["rays"]:
            traversed, hit = _ray_cells(ray, obs["width"], obs["height"])
            free.update(traversed)
            signature = tuple(ray[k] for k in RAY_KEYS)
            for cell in traversed:
                signatures.setdefault(cell, set()).add(signature)
            if hit is not None:
                occupied.add(hit)
        for cell in free - occupied:
            self.cells[cell] = "free"
        for cell in occupied:
            self.cells[cell] = "occupied"
        self.cells[_point(obs)] = "free"  # public odometry, not invented sensor evidence
        for key, edge in list(self.blocked_edges.items()):
            target = None if edge["target"] is None else tuple(edge["target"])
            fresh = signatures.get(target, set()) - {tuple(r) for r in edge["rays"]}
            if target not in occupied and fresh:
                del self.blocked_edges[key]
                self.recorder.write(kind="navigation_refutation", session=obs["session"], seq=obs["seq"],
                                    edge=edge, source="new_free_ray_measurement")
        self._frame_signatures = signatures
        self.scanned.setdefault(_point(obs), set()).add(obs["yaw"])
        self.visits[_point(obs)] += 1

    def _consequence(self, obs):
        previous, before = self.last_decision, self.last_observation
        result = dict(resolution="no_action", matched=False, confirmed=False)
        if previous is None or previous["action"] not in ("move", "scan"):
            return result
        outcome = obs["outcome"]
        matched = _matched(obs, previous, before)
        confirmed = False
        if not matched:
            self.valid = False
            self.stopped = "Recibo pendiente o no vinculado; no se autoriza otra acción."
            result["resolution"] = "unresolved"
        elif previous["action"] == "scan":
            confirmed = outcome["status"] == "scanned"
            result["resolution"] = "confirmed" if confirmed else "cancelled"
        else:
            origin, actual = _point(before), _point(obs)
            command = previous["command"]
            confirmed = (outcome["status"] == "arrived" and outcome["distanceMm"] <= 200
                         and _distance(origin, actual) == 1)
            result["resolution"] = "confirmed" if confirmed else "failed"
            if confirmed:
                self.metrics["confirmedMoves"] += 1
                predicted = self._target(origin, command)
                self.blocked_edges.pop((origin, command), None)
                if obs["profile"] in ("learn", "cold"):
                    self._learn(obs, origin, command, actual)
                elif predicted != actual:
                    self.valid = False
                    self.stopped = "El movimiento confirmado contradice el modelo congelado; reinicio requerido."
            else:
                self.metrics["failedMoves"] += 1
                key = (origin, command)
                self.metrics["repeatedFailures"] += int(key in self.failed_edges)
                self.failed_edges.add(key)
                target = self._target(origin, command)
                self.blocked_edges[key] = dict(origin=list(origin), command=command,
                    target=None if target is None else list(target),
                    infer_cell=outcome["status"] == "blocked" and target is not None and actual == origin,
                    rays=[list(r) for r in sorted(getattr(self, "_frame_signatures", {}).get(target, set()))])
                if actual != origin or outcome["status"] == "arrived":
                    self.valid = False
                    self.stopped = "Movimiento sin llegada unitaria confirmada; pose o actuador incierto."
        result.update(matched=matched, confirmed=confirmed)
        self.recorder.write(kind="navigation_consequence", session=obs["session"], seq=obs["seq"],
                            previous_seq=previous["seq"], outcome=outcome, **result)
        return result

    def _learn(self, obs, origin, command, actual):
        before, after = _projection(origin, actual)
        signature = _signature(origin, command, actual)
        accepted, detail = False, "duplicate_projection_no_support"
        if signature not in self.seen_projections:
            self.budget.consume("navigation_calibration", 1)
            epoch = self.model["epoch"]
            try:
                learned = self.core.observe(before, command, after, budget=self.budget)
            except ValueError as exc:
                # The core is authoritative about receipt identity, including
                # receipts reconstructed by restore. Never inflate support.
                if "duplicate transition receipt" not in str(exc):
                    raise
            else:
                accepted, detail = True, "confirmed_unit_motion_empty_plane_projection"
                self._refresh_model()
                if self.model["epoch"] != epoch:
                    self.blocked_edges.clear()
                    self.metrics["dynamicsChanges"] += 1
                self.metrics["calibrationAccepted"] += 1
                self.calibrations.append(dict(outcome=obs["outcome"], before=before, after=after,
                                              action=command, learning=learned))
        if not accepted:
            self.metrics["duplicateProjections"] += 1
        self.recorder.write(kind="navigation_calibration", session=obs["session"], seq=obs["seq"],
                            accepted=accepted, detail=detail, outcome=obs["outcome"],
                            before=before, action=command, after=after,
                            projection="calibración; plano libre 16×16, sin afirmaciones sobre paredes físicas",
                            support=self.model["support"], modelHash=self.checkpoint["sha256"])

    def _free(self, obs):
        free = {p for p, state in self.cells.items() if state == "free"} - self._inferred()
        # Even an edge without a measured destination must veto a known effect
        # once acquired. The planner supports cells, so exclude it conservatively.
        for (origin, command), edge in self.blocked_edges.items():
            target = self._target(origin, command)
            if target is not None:
                free.discard(target)
        free.add(_point(obs))
        return free

    def _route(self, obs, target):
        free = self._free(obs)
        if target not in free or target == _point(obs):
            return None
        self.budget.consume("navigation_projection", obs["width"] * obs["height"])
        projected = dict(schema="echo5.interaction.observation.v1", width=obs["width"],
            height=obs["height"], walls=[[z, x] for z in range(obs["height"]) for x in range(obs["width"])
                                      if (x, z) not in free],
            position=[obs["z"], obs["x"]], goal=[target[1], target[0]], actions=[0, 1, 2, 3], tick=0)
        result = self.core.plan(projected, budget=self.budget, max_depth=obs["width"] * obs["height"])
        self.recorder.write(kind="navigation_plan", session=obs["session"], seq=obs["seq"],
                            projection="solo celdas libres recordadas; exclusiones no son paredes medidas",
                            goal=_xy(target), freeCells=len(free), result=result,
                            modelHash=self.checkpoint["sha256"])
        if result.get("reason") == "budget_exhausted":
            raise BudgetExceeded("core planning budget exhausted")
        if result["status"] == "planned":
            return result
        return None

    def _scan(self, obs):
        origin = _point(obs)
        seen = self.scanned.get(origin, set())
        unknown = [(yaw, p) for yaw, p in zip((0, 90, 180, 270), _neighbors(origin))
                   if _inside(p, obs["width"], obs["height"]) and p not in self.cells]
        candidates = [yaw for yaw, _ in unknown if yaw not in seen]
        if not candidates and any(key[0] == origin for key in self.blocked_edges):
            candidates = [yaw for yaw in (0, 90, 180, 270) if yaw not in seen]
        if candidates:
            return self._decision(obs, "scan", "Girar la cámara para medir espacio todavía incierto.", yaw=candidates[0])
        return None

    def _probe_options(self, origin, free, *, allow_ambiguous=False):
        ranked = []
        for command, candidates in enumerate(self.model["hypotheses"]):
            self.budget.consume("navigation_probe", 1 + len(candidates))
            if self.model["support"][command] >= 2 or not candidates or (origin, command) in self.blocked_edges:
                continue
            destinations = {(origin[0] + dc, origin[1] + dr) for dr, dc in candidates if dr or dc}
            target = self._target(origin, command)
            if target is not None:
                if target not in free or _signature(origin, command, target) in self.seen_projections:
                    continue
            safe = bool(destinations) and destinations <= free
            if not safe and not allow_ambiguous:
                continue
            ranked.append((not safe, self.attempts[(origin, command)],
                           self.model["support"][command], -len(destinations & free), command))
        return ranked

    def _probe(self, obs, *, allow_ambiguous=False):
        ranked = self._probe_options(_point(obs), self._free(obs), allow_ambiguous=allow_ambiguous)
        if not ranked:
            return None
        command = min(ranked)[-1]
        return self._decision(obs, "move", "Muestra física para identificar un comando; efecto aún no adquirido con dos recibos.",
                              command=command)

    def _reposition_for_probe(self, obs):
        """Replay confirmed local edges to a NEW sample; do not inflate support.

        A single receipt is enough to revisit that observed edge. Generalized
        planning still requires two independent samples in the unchanged core.
        Only this session's physical calibration receipts form the graph.
        """
        if obs['profile'] == 'no_memory':
            return None
        origin, free = _point(obs), self._free(obs)
        edges = {}
        for sample in self.calibrations:
            self.budget.consume('navigation_reposition_receipt')
            receipt = sample['outcome']
            source = (receipt['fromX'], receipt['fromZ'])
            target = (receipt['x'], receipt['z'])
            command = receipt['command']
            if (receipt['session'] == obs['session'] and receipt['status'] == 'arrived'
                    and source in free and target in free
                    and (source, command) not in self.blocked_edges
                    and self._target(source, command) == target):
                edges.setdefault(source, set()).add((command, target))
        frontier, parents = deque([origin]), {origin: None}
        while frontier:
            node = frontier.popleft()
            self.budget.consume('navigation_reposition_node')
            if node != origin and self._probe_options(node, free):
                actions, positions, cursor = [], [node], node
                while cursor != origin:
                    self.budget.consume('navigation_reposition_extract')
                    previous, command = parents[cursor]
                    actions.append(command);positions.append(previous);cursor = previous
                route = dict(status='planned', actions=list(reversed(actions)),
                             positions=[[z,x] for x,z in reversed(positions)],
                             source='confirmed_session_receipts')
                self.recorder.write(kind='navigation_calibration_reposition', session=obs['session'],
                                    seq=obs['seq'], route=route, sample_origin=list(node),
                                    support=list(self.model['support']))
                return self._decision(obs, 'move',
                    'Revisitar un recorrido confirmado para adquirir una muestra nueva de calibración.', route=route)
            for command, target in sorted(edges.get(node, ())):
                self.budget.consume('navigation_reposition_edge')
                if target not in parents:
                    parents[target] = (node, command)
                    frontier.append(target)
        return None

    def _choose(self, obs):
        if self.stopped:
            return self._decision(obs, "wait", self.stopped)
        if obs["completed"]:
            return self._decision(obs, "done", "Meta confirmada por pose y finalización pública.")
        if self.model["out_of_model"]:
            return self._decision(obs, "wait", "Evidencia fuera de la clase de hipótesis del núcleo.")
        learning = obs["profile"] in ("learn", "cold") and any(s < 2 for s in self.model["support"])
        if learning:
            probe = self._probe(obs)
            if probe:
                return probe
            scan = self._scan(obs)
            if scan:
                return scan
            reposition = self._reposition_for_probe(obs)
            if reposition:
                return reposition
            probe = self._probe(obs, allow_ambiguous=True)
            if probe:
                return probe
        route = self._route(obs, (obs["goalX"], obs["goalZ"]))
        if route:
            return self._decision(obs, "move", "PLAN del núcleo sobre el mapa observado y efectos adquiridos.", route=route)
        scan = self._scan(obs)
        if scan:
            return scan
        origin, goal, free = _point(obs), (obs["goalX"], obs["goalZ"]), self._free(obs)
        frontiers = [p for p in free if p != origin and any(
            _inside(n, obs["width"], obs["height"]) and n not in self.cells for n in _neighbors(p))]
        if learning:
            frontiers += [p for p in free if p != origin and p not in frontiers]
        self.budget.consume("navigation_frontiers", len(free))
        frontiers.sort(key=lambda p: (self.visits[p] > 3, _distance(p, goal), self.visits[p], _distance(origin, p), p))
        # A cap of eight unreachable candidates is not evidence of no route.
        # Work remains bounded by the shared budget and finite observed map.
        for target in dict.fromkeys(frontiers):
            route = self._route(obs, target)
            if route:
                return self._decision(obs, "move", "Explorar una frontera mediante PLAN aprendido.", route=route)
        return self._decision(obs, "wait", "No hay ruta respaldada ni nueva orientación de cámara disponible.")

    def _decision(self, obs, action, reason, *, command=-1, yaw=None, route=None):
        plan = [] if route is None else [_xy((col, row)) for row, col in route["positions"][1:]]
        if route is not None:
            command = route["actions"][0]
        hypotheses = [f"Comando {a}: candidatos (Δz,Δx)={json.dumps(candidates, separators=(',', ':'))}; "
                      f"soporte independiente={self.model['support'][a]}; época={self.model['epoch']}."
                      for a, candidates in enumerate(self.model["hypotheses"])]
        prediction = "Esperar una observación física; ninguna consecuencia se da por realizada."
        if action == "move":
            target = self._target(_point(obs), command)
            prediction = (f"Hipótesis adquirida: destino ({target[0]},{target[1]}); requiere recibo físico."
                          if target is not None else "Efecto del comando desconocido; la muestra física discriminará hipótesis.")
        elif action == "scan":
            prediction = f"Nueva medida del frustum con yaw={yaw}; no implica desplazamiento."
        inferred = self._inferred() - {_point(obs)}
        cells = dict(self.cells)
        for point in inferred:
            if _inside(point, obs["width"], obs["height"]) and cells.get(point) != "occupied":
                cells[point] = "inferred"
        return dict(schema="echo.navigation.decision.v1", session=obs["session"], seq=obs["seq"],
                    action=action, command=command, yaw=obs["yaw"] if yaw is None else yaw,
                    reason=reason, prediction=prediction, hypotheses=hypotheses,
                    evidence=[f"Odometría pública=({obs['x']},{obs['z']}); rayos actuales={len(obs['rays'])}.",
                              f"Celdas medidas={len(self.cells)}; aristas fallidas recordadas={len(self.blocked_edges)}.",
                              f"Recibos de calibración en checkpoint={len(self.checkpoint['payload']['evidence'])}."],
                    cells=[dict(x=p[0], z=p[1], state=s) for p, s in sorted(cells.items())], plan=plan,
                    support=list(self.model["support"]),
                    learnedActions=sum(s >= 2 and len(h) == 1 for s, h in zip(self.model["support"], self.model["hypotheses"])),
                    budgetUsed=self.budget.used, budgetRemaining=self.budget.remaining,
                    modelHash=self.checkpoint["sha256"], journal=self.journal_path)

    def _ingest(self, obs, *, terminal=False):
        self.recorder.write(kind="navigation_observation", terminal=terminal, observation=obs)
        try:
            self._remember(obs)
            consequence = self._consequence(obs)
        except BudgetExceeded:
            self.valid = False
            self.stopped = "Presupuesto lógico agotado; no se autoriza otra acción."
            # Preserve the physical receipt even when no learning/map work fits.
            consequence = dict(resolution="budget_exhausted", matched=False, confirmed=False)
            self.recorder.write(kind="navigation_consequence", session=obs["session"], seq=obs["seq"],
                                outcome=obs["outcome"], **consequence)
        return consequence

    def decide(self, obs):
        self._ingest(obs)
        try:
            self.budget.consume("navigation_decision")
            result = self._choose(obs)
        except BudgetExceeded:
            self.valid = False
            self.stopped = "Presupuesto lógico agotado; no se autoriza otra acción."
            result = self._decision(obs, "wait", self.stopped)
        self.metrics["decisions"] += 1
        if result["action"] in ("move", "scan"):
            self.metrics["moves" if result["action"] == "move" else "scans"] += 1
        if result["action"] == "move":
            self.attempts[(_point(obs), result["command"])] += 1
        self.recorder.write(kind="navigation_decision", decision=result, budget=self.budget.snapshot())
        self.last_observation, self.last_decision = obs, result
        return result

    def finish(self, obs, *, same_observation=False):
        if same_observation and self.last_decision["action"] in ("wait", "done"):
            # The last receipt was already consumed by /navigate. Never replay it.
            consequence = dict(resolution="no_action", matched=False, confirmed=False)
            self.recorder.write(kind="navigation_observation", terminal=True, observation=obs)
        elif same_observation:
            self.valid = False
            consequence = dict(resolution="unresolved", matched=False, confirmed=False)
            self.recorder.write(kind="navigation_observation", terminal=True, observation=obs)
            self.recorder.write(kind="navigation_consequence", session=obs["session"], seq=obs["seq"],
                                outcome=obs["outcome"], **consequence)
        else:
            consequence = self._ingest(obs, terminal=True)
        result = dict(schema="echo.navigation.finish.v1", session=obs["session"], seq=obs["seq"],
                      status="closed", **consequence, pending=consequence["resolution"] == "unresolved",
                      completed=obs["completed"] and self.valid, support=list(self.model["support"]),
                      modelHash=self.checkpoint["sha256"], budgetUsed=self.budget.used,
                      budgetRemaining=self.budget.remaining, metrics=dict(self.metrics), journal=self.journal_path)
        return result


@dataclass
class Session:
    identity: tuple
    agent: NavigationAgent
    recorder: Recorder
    acquisition: list
    model_file_hash: str | None
    seq: int = -1
    fingerprint: bytes = b""
    response: bytes = b""
    requests: int = 0
    failed: bool = False
    closed: bool = False
    terminal: bool = False
    saved: bool = False
    finish_fingerprint: bytes = b""
    finish_response: bytes = b""


def _identity(obs):
    return tuple(obs[k] for k in ("world", "profile", "width", "height", "goalX", "goalZ"))


def _atomic(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("xb") as handle:
            os.chmod(temporary, 0o600)
            handle.write(_bytes(value))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


class NavigationService:
    """Serialized, bounded sessions. Each artifact root is an evidence namespace.

    The server uses its own default root/model. CPU evaluators must pass a
    separate artifact_root and model_path. No global model or map cache exists.
    """

    def __init__(self, artifact_root, model_path=None, *, agent_type=NavigationAgent):
        # Optional actor adapter; the default and the original server stay unchanged.
        self.agent_type = agent_type
        self.artifact_root = Path(artifact_root).expanduser().resolve()
        self.model_path = (self.artifact_root / "model.json" if model_path is None
                           else Path(model_path).expanduser().resolve())
        self.sessions = {}
        self.lock = threading.Lock()
        self.closed = False
        # A fixed-size conservative tombstone filter prevents an evicted token
        # from starting again. False positives reject; they never replay actions.
        self._session_filter = bytearray(131072)

    def _token_bits(self, token):
        digest = hashlib.sha256(token.encode()).digest()
        return [int.from_bytes(digest[i:i + 4], "big") % (len(self._session_filter) * 8)
                for i in (0, 4, 8, 12)]

    def _retired(self, token):
        return all(self._session_filter[bit // 8] & (1 << (bit % 8)) for bit in self._token_bits(token))

    def _register_token(self, token):
        for bit in self._token_bits(token):
            self._session_filter[bit // 8] |= 1 << (bit % 8)

    def _load_core(self, obs, budget):
        if obs["profile"] == "cold":
            return HypothesisAgent(), [], None, False
        if not self.model_path.exists():
            _require(obs["profile"] == "learn", "trained_model_required", 409)
            return HypothesisAgent(), [], None, False
        try:
            _require(self.model_path.stat().st_size <= 16_000_000, "model_too_large", 409)
            raw = self.model_path.read_bytes()
            envelope = _json(raw)
            _fields(envelope, ("schema", "checkpoint", "acquisition"))
            _require(envelope["schema"] == "echo.navigation.model.v1"
                     and type(envelope["acquisition"]) is list, "invalid_navigation_model", 409)
            core = HypothesisAgent.restore(envelope["checkpoint"], budget=budget)
            checkpoint = core.checkpoint()
            _require(checkpoint["payload"]["mode"] == "active", "invalid_navigation_model", 409)
            for record in checkpoint["payload"]["evidence"]:
                origin = record["before"]["position"][::-1]
                actual = record["after"]["position"][::-1]
                before, after = _projection(origin, actual)
                _require(record["before"] == before and record["after"] == after
                         and _distance(origin, actual) == 1, "non_navigation_evidence", 409)
            return core, envelope["acquisition"], hashlib.sha256(raw).hexdigest(), True
        except (ValueError, KeyError, TypeError, OSError, BudgetExceeded) as exc:
            if isinstance(exc, ProtocolError) and exc.status == 409:
                raise
            raise ProtocolError(409, "invalid_navigation_model") from exc

    def _map_path(self, world):
        return self.artifact_root / "maps" / (hashlib.sha256(world.encode()).hexdigest() + ".json")

    def _load_map(self, obs, agent):
        path = self._map_path(obs["world"])
        if obs["profile"] != "remembered" or not path.exists():
            return
        try:
            _require(path.stat().st_size <= 1_000_000, "invalid_remembered_map", 409)
            memory = _json(path.read_bytes())
            _fields(memory, ("schema", "world", "width", "height", "cells", "edges"))
            _require(memory["schema"] == "echo.navigation.map.v1" and memory["world"] == obs["world"]
                     and memory["width"] == obs["width"] and memory["height"] == obs["height"],
                     "remembered_world_changed", 409)
            _require(type(memory["cells"]) is list and len(memory["cells"]) <= obs["width"] * obs["height"])
            for cell in memory["cells"]:
                _fields(cell, ("x", "z", "state"))
                _integer(cell["x"], 0, obs["width"] - 1)
                _integer(cell["z"], 0, obs["height"] - 1)
                _require(cell["state"] in ("free", "occupied") and _point(cell) not in agent.cells)
                agent.cells[_point(cell)] = cell["state"]
            _require(type(memory["edges"]) is list and len(memory["edges"]) <= obs["width"] * obs["height"] * 4)
            for edge in memory["edges"]:
                _fields(edge, ("origin", "command", "target", "infer_cell", "rays"))
                _integer(edge["command"], 0, 3)
                for point in (edge["origin"], edge["target"]):
                    if point is not None:
                        _require(type(point) is list and len(point) == 2)
                        _integer(point[0], -1, obs["width"])
                        _integer(point[1], -1, obs["height"])
                _require(edge["origin"] is not None and type(edge["infer_cell"]) is bool
                         and type(edge["rays"]) is list and len(edge["rays"]) <= 65)
                for row in edge["rays"]:
                    _require(type(row) is list and len(row) == 5
                             and all(type(n) is int for n in row[:4]) and type(row[4]) is bool)
                agent.blocked_edges[(tuple(edge["origin"]), edge["command"])] = edge
            agent.budget.consume("navigation_map_restore", 1 + len(memory["cells"]) + len(memory["edges"]))
            agent.recorder.write(kind="navigation_map_restore", world=obs["world"],
                                 cells=len(agent.cells), inferredEdges=len(agent.blocked_edges))
        except (ValueError, KeyError, TypeError, OSError) as exc:
            raise ProtocolError(409, "invalid_remembered_map") from exc

    def _save_map(self, obs, session):
        if obs["profile"] not in ("learn", "trained", "remembered"):
            return
        agent = session.agent
        _atomic(self._map_path(obs["world"]), dict(schema="echo.navigation.map.v1", world=obs["world"],
                width=obs["width"], height=obs["height"],
                cells=[dict(x=p[0], z=p[1], state=s) for p, s in sorted(agent.cells.items())],
                edges=list(agent.blocked_edges.values())))

    def _save_model(self, obs, session):
        agent = session.agent
        if (obs["profile"] != "learn" or not obs["completed"] or not agent.valid
                or session.saved or not agent.calibrations):
            return
        current = hashlib.sha256(self.model_path.read_bytes()).hexdigest() if self.model_path.exists() else None
        _require(current == session.model_file_hash, "model_changed_during_acquisition", 409)
        acquisition = session.acquisition + [dict(session=obs["session"], world=obs["world"],
            completed=True, journal=agent.journal_path, calibrations=agent.calibrations,
            projection="confirmed unit odometry; empty 16x16 plane; independent tick 0 to 1",
            modelHash=agent.checkpoint["sha256"], metrics=dict(agent.metrics))]
        payload = dict(schema="echo.navigation.model.v1", checkpoint=agent.checkpoint, acquisition=acquisition)
        _atomic(self.model_path, payload)
        session.saved = True
        agent.recorder.write(kind="navigation_model_saved", modelHash=agent.checkpoint["sha256"],
                             observations=len(agent.checkpoint["payload"]["evidence"]),
                             session=obs["session"], world=obs["world"])

    def _new_session(self, obs):
        _require(obs["seq"] == 0 and not obs["outcome"]["present"], "invalid_initial_observation", 409)
        _require(not self._retired(obs["session"]), "retired_session", 409)
        _require(sum(not s.closed for s in self.sessions.values()) < SESSION_LIMIT, "session_limit", 503)
        # Bound concurrent writers; never silently overwrite another acquisition.
        for existing in self.sessions.values():
            if existing.closed or existing.terminal:
                continue
            if obs["profile"] == "learn" and existing.identity[1] == "learn":
                raise ProtocolError(409, "acquisition_already_active")
            if obs["profile"] == "remembered" and existing.identity[:2] == (obs["world"], "remembered"):
                raise ProtocolError(409, "remembered_world_already_active")
        budget = Budget(WORK_LIMIT)
        core, provenance, file_hash, restored = self._load_core(obs, budget)
        journal = self.artifact_root / ("run-" + uuid.uuid4().hex) / "thought.jsonl"
        recorder = Recorder(journal)
        try:
            agent = self.agent_type(core=core, budget=budget, recorder=recorder, journal_path=journal)
            agent.metrics["restored"] = int(restored)
            self._load_map(obs, agent)
            recorder.write(kind="navigation_session", session=obs["session"], world=obs["world"],
                           profile=obs["profile"], restored=restored, modelHash=agent.checkpoint["sha256"],
                           support=agent.model["support"], budget=budget.snapshot())
        except Exception:
            recorder.close()
            raise
        session = Session(_identity(obs), agent, recorder, provenance, file_hash)
        self.sessions[obs["session"]] = session
        self._register_token(obs["session"])
        return session

    def process(self, raw):
        obs = decode_observation(raw)
        fingerprint = hashlib.sha256(raw).digest()
        with self.lock:
            _require(not self.closed, "service_closed", 503)
            session = self.sessions.get(obs["session"])
            if session is None:
                session = self._new_session(obs)
            else:
                _require(not session.closed, "session_closed", 409)
                _require(not session.failed, "session_failed_restart_required", 503)
                if obs["seq"] == session.seq:
                    _require(fingerprint == session.fingerprint, "changed_retry", 409)
                    return session.response
                _require(not session.terminal, "episode_terminal", 409)
                _require(obs["seq"] == session.seq + 1, "stale_or_skipped_sequence", 409)
                _require(_identity(obs) == session.identity, "session_scenario_changed", 409)
                _require(session.requests < DECISION_LIMIT, "request_limit", 429)
                previous, before = session.agent.last_decision, session.agent.last_observation
                if previous["action"] in ("move", "scan"):
                    _require(_matched(obs, previous, before), "outcome_mismatch", 409)
                else:
                    _require(_point(obs) == _point(before)
                             and obs["outcome"] == before["outcome"], "unsolicited_outcome", 409)
            try:
                result = session.agent.decide(obs)
                self._save_model(obs, session)
                self._save_map(obs, session)
                response = _bytes(result)
            except Exception:
                session.failed = True
                raise
            session.seq, session.fingerprint, session.response = obs["seq"], fingerprint, response
            session.requests += 1
            session.terminal = result["action"] == "done"
            return response

    def finish(self, raw):
        obs = decode_observation(raw)
        fingerprint = hashlib.sha256(raw).digest()
        with self.lock:
            _require(not self.closed, "service_closed", 503)
            session = self.sessions.get(obs["session"])
            _require(session is not None, "unknown_session", 404)
            if session.closed:
                _require(fingerprint == session.finish_fingerprint, "changed_finish", 409)
                return session.finish_response
            _require(not session.failed, "session_failed_restart_required", 503)
            _require(_identity(obs) == session.identity, "session_scenario_changed", 409)
            _require(obs["seq"] in (session.seq, session.seq + 1), "invalid_finish_sequence", 409)
            same = fingerprint == session.fingerprint
            # SAME seq is precisely the previous observation: pending proposal
            # may never have reached the client. NEXT seq carries its outcome.
            _require(obs["seq"] != session.seq or same, "changed_finish_observation", 409)
            previous, before = session.agent.last_decision, session.agent.last_observation
            if not same and previous["action"] in ("wait", "done"):
                _require(_point(obs) == _point(before) and obs["outcome"] == before["outcome"],
                         "unsolicited_outcome", 409)
            try:
                result = session.agent.finish(obs, same_observation=same)
                self._save_model(obs, session)
                self._save_map(obs, session)
                result["modelSaved"] = session.saved
                session.recorder.write(kind="navigation_finish", result=result, budget=session.agent.budget.snapshot())
                response = _bytes(result)
                session.recorder.close()
            except Exception:
                session.failed = True
                raise
            session.finish_fingerprint, session.finish_response = fingerprint, response
            session.closed = True
            # Recent terminal replies remain byte-idempotent, without retaining
            # whole checkpoint histories for every completed episode.
            session.agent = None
            session.recorder = None
            session.acquisition = []
            self.sessions.pop(obs["session"])
            self.sessions[obs["session"]] = session
            closed = [token for token, value in self.sessions.items() if value.closed]
            for token in closed[:-CLOSED_CACHE_LIMIT]:
                del self.sessions[token]
            return response

    def close(self):
        with self.lock:
            if not self.closed:
                self.closed = True
                for session in self.sessions.values():
                    if session.recorder is not None:
                        session.recorder.close()
