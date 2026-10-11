"""Contract tests using the installed ECHO-5 planner and a public-only test world."""

import copy
import http.client
import importlib
import json
import math
from pathlib import Path
import socket
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
BRIDGE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BRIDGE))


def encoded(value):
    return json.dumps(value, separators=(",", ":"), allow_nan=False).encode()


def obj(identifier, kind, x, z, **extra):
    return dict(id=identifier, kind=kind, label=kind, x=x, z=z,
                inspected=extra.get("inspected", False),
                compatible=extra.get("compatible", False),
                active=extra.get("active", False), locked=extra.get("locked", False))


def empty_outcome():
    return dict(present=False, session="", seq=-1, action="", objectId="", status="",
                targetX=0, targetZ=0, actualX=0, actualZ=0, distanceMm=0)


def action_outcome(decision, actual, *, status=None, distance_mm=0):
    return dict(present=True, session=decision["session"], seq=decision["seq"],
                action=decision["action"], objectId=decision["objectId"],
                status=status or ("arrived" if decision["action"] == "move" else "applied"),
                targetX=decision["x"], targetZ=decision["z"], actualX=actual[0], actualZ=actual[1],
                distanceMm=distance_mm)


def observation(**extra):
    value = dict(schema="echo.station.observation.v1", session="test-random-session",
                 seq=0, mission="delivery", width=15, height=13, x=2, z=2,
                 battery=100, inventory=[],
                 cells=[dict(x=x, z=z, blocked=False)
                        for x in range(1, 6) for z in range(1, 6)],
                 objects=[obj("charger-home", "charger", 2, 2)],
                 lastResult="", completed=False, outcome=empty_outcome())
    value.update(extra)
    return value


class PublicWorld:
    """Deterministic fixture. Only observation() crosses the agent boundary.

    Radius-two visibility, a locked corridor door, persistent object IDs,
    three inventory slots, and one energy unit per movement/action.
    """

    def __init__(self, mission, variant=0):
        self.mission = mission
        self.session = f"fixture-{mission}-{variant}"
        self.seq = 0
        self.pos = (2, 2)
        self.energy = 80
        self.inventory = []
        self.result = ""
        self.outcome = empty_outcome()
        self.completed = False
        self.recharges = 0
        self.actions = []
        self.objects = [obj("home", "charger", 2, 2),
                        obj("key", "key", 4, 3),
                        obj("bad-cell", "battery", 5, 8),
                        obj("good-cell", "battery", 3, 9, compatible=True),
                        obj("gate", "door", 7, 6, locked=mission != "delivery"),
                        obj("pump", "pump", 11, 9), obj("valve", "valve", 11, 4),
                        obj("parcel", "parcel", 4, 10),
                        obj("destination", "delivery", 11, 3),
                        obj("console", "terminal", 4, 6)]
        self.walls = {(x, z) for x in range(15) for z in range(13)
                      if x in (0, 14) or z in (0, 12) or (x == 7 and z != 6)}
        if mission == "detour":
            self.walls.update({(9, z) for z in range(2, 10)})
        if variant:
            # Same public contract; swapped battery compatibility and more obstacles.
            self.objects[2]["compatible"] = True
            self.objects[3]["compatible"] = False
            self.walls.update({(5, 4), (5, 5), (10, 7)})

    def blocked(self, p):
        if p in self.walls:
            return True
        for item in self.objects:
            if (item["x"], item["z"]) != p:
                continue
            if item["kind"] in ("charger", "delivery"):
                continue
            if item["kind"] == "door" and not item["locked"]:
                continue
            if item["kind"] in ("key", "battery", "parcel") and item["active"]:
                continue
            return True
        return False

    def observation(self):
        visible = {(x, z) for x in range(15) for z in range(13)
                   if abs(x - self.pos[0]) + abs(z - self.pos[1]) <= 2}
        objects = [copy.deepcopy(o) for o in self.objects if (o["x"], o["z"]) in visible]
        for o in objects:
            if not o["inspected"]:
                o["compatible"] = False
        return observation(session=self.session, seq=self.seq, mission=self.mission,
                           x=self.pos[0], z=self.pos[1], battery=self.energy,
                           inventory=list(self.inventory), completed=self.completed,
                           cells=[dict(x=x, z=z, blocked=self.blocked((x, z)))
                                  for x, z in sorted(visible)],
                           objects=objects, lastResult=self.result, outcome=copy.deepcopy(self.outcome))

    def step(self, decision):
        action = decision["action"]
        self.actions.append((action, decision["objectId"]))
        self.result = "ok"
        if action == "move":
            dest = (decision["x"], decision["z"])
            assert abs(dest[0] - self.pos[0]) + abs(dest[1] - self.pos[1]) == 1
            assert not self.blocked(dest), ("blocked move", dest, decision)
            assert self.energy > 0
            self.pos = dest
            self.energy -= 1
        elif action in ("inspect", "interact"):
            item = next(o for o in self.objects if o["id"] == decision["objectId"])
            assert abs(item["x"] - self.pos[0]) + abs(item["z"] - self.pos[1]) <= 1
            kind = item["kind"]
            if action == "inspect":
                item["inspected"] = True
            elif kind == "charger":
                self.energy = 100
                self.recharges += 1
            elif kind in ("key", "battery", "parcel"):
                assert not item["active"] and len(self.inventory) < 3
                if kind == "battery":
                    assert item["inspected"] and item["compatible"]
                self.inventory.append(kind)
                item["active"] = True
            elif kind == "door":
                assert "key" in self.inventory and item["locked"]
                self.inventory.remove("key")
                item["locked"] = False
                item["active"] = True
            elif kind == "pump":
                assert "battery" in self.inventory
                self.inventory.remove("battery")
                item["active"] = True
            elif kind == "valve":
                assert next(o for o in self.objects if o["id"] == "pump")["active"]
                item["active"] = True
            elif kind == "delivery":
                assert "parcel" in self.inventory
                self.inventory.remove("parcel")
                item["active"] = True
            else:
                raise AssertionError(("undefined interaction", decision))
            if kind != "charger":
                self.energy = max(0, self.energy - 1)
        elif action == "done":
            assert self.completed
        elif action == "wait":
            raise AssertionError(("unexpected stalled fixture", decision))
        target = "destination" if self.mission == "delivery" else "valve"
        self.completed = (next(o for o in self.objects if o["id"] == target)["active"]
                          and self.pos == (2, 2))
        self.outcome = action_outcome(decision, self.pos)
        self.seq += 1


class UnityWorld(PublicWorld):
    """The 2026-10-11 StationWorld/StationEditor public behavior, including sight.

    Geometry lives exclusively in this evaluator fixture, never in the adapter.
    Variant 1 starts with low charge to exercise recharge and mission resumption.
    """

    def __init__(self, mission, variant=0):
        super().__init__(mission, variant)
        self.session = f"unity-{mission}-{variant}"
        self.energy = 100 if variant == 0 else 24
        self.collected = set()
        self.detour = False
        self.objects = [obj("charging-dock", "charger", 2, 2),
                        obj("service-key", "key", 2, 10),
                        obj("battery-a", "battery", 3, 8, compatible=True),
                        obj("battery-b", "battery", 2, 8),
                        obj("greenhouse-door", "door", 9, 9, locked=True),
                        obj("water-pump", "pump", 12, 9),
                        obj("water-valve", "valve", 11, 10),
                        obj("cargo-parcel", "parcel", 3, 10),
                        obj("delivery-terminal", "delivery", 12, 3)]
        self.objects += [obj(f"decor-{x}-{z}", "scenery", x, z) for x, z in
                         ((11, 8), (13, 10), (7, 10), (1, 8), (10, 4), (8, 2), (1, 4), (13, 4))]
        self.walls = {(x, z) for x in range(15) for z in range(13)
                      if x in (0, 14) or z in (0, 12) or (x == 5 and z not in (3, 9))
                      or (z == 6 and x not in (3, 7)) or (x == 9 and z > 6 and z != 9)}

    def blocked(self, p):
        if p in self.walls or (self.detour and p == (5, 9)):
            return True
        return any(o["id"] not in self.collected and (o["x"], o["z"]) == p and
                   (o["locked"] if o["kind"] == "door" else o["kind"] not in ("charger", "delivery", "key"))
                   for o in self.objects)

    def visible(self, p):
        if abs(p[0] - self.pos[0]) + abs(p[1] - self.pos[1]) > 5:
            return False
        count = max(1, math.ceil(math.dist(self.pos, p) * 8))
        for i in range(1, count):
            q = (round(self.pos[0] + (p[0] - self.pos[0]) * i / count),
                 round(self.pos[1] + (p[1] - self.pos[1]) * i / count))
            if q in (p, self.pos):
                continue
            if q in self.walls or any(o["kind"] == "door" and o["locked"] and
                                      (o["x"], o["z"]) == q for o in self.objects):
                return False
        return True

    def observation(self):
        objects = [copy.deepcopy(o) for o in self.objects
                   if o["id"] not in self.collected and self.visible((o["x"], o["z"]))]
        for o in objects:
            if not o["inspected"]:
                o["compatible"] = False
        return observation(session=self.session, seq=self.seq, mission=self.mission,
                           x=self.pos[0], z=self.pos[1], battery=math.ceil(self.energy),
                           inventory=list(self.inventory), completed=self.completed,
                           cells=[dict(x=x, z=z, blocked=self.blocked((x, z)))
                                  for z in range(13) for x in range(15) if self.visible((x, z))],
                           objects=objects, lastResult=self.result, outcome=copy.deepcopy(self.outcome))

    def step(self, d):
        action = d["action"]
        self.actions.append((action, d["objectId"]))
        self.result = "observed action result"
        if action == "move":
            dest = (d["x"], d["z"])
            assert abs(dest[0] - self.pos[0]) + abs(dest[1] - self.pos[1]) == 1
            if not self.blocked(dest):
                self.pos = dest
            else:
                self.result = "Movimiento bloqueado"
            self.energy = max(0, self.energy - .28)
            if self.mission == "detour" and "battery" in self.inventory:
                self.detour = True
        elif action in ("inspect", "interact"):
            item = next(o for o in self.objects if o["id"] == d["objectId"])
            assert item["id"] not in self.collected
            assert abs(item["x"] - self.pos[0]) + abs(item["z"] - self.pos[1]) <= 1
            self.energy = max(0, self.energy - .12)
            kind = item["kind"]
            if action == "inspect":
                item["inspected"] = True
            elif kind in ("key", "parcel", "battery"):
                assert len(self.inventory) < 3
                if kind == "battery":
                    assert item["inspected"] and item["compatible"]
                self.collected.add(item["id"])
                self.inventory.append(kind)
            elif kind == "charger":
                self.energy = 100
                self.recharges += 1
            elif kind == "door":
                assert "key" in self.inventory
                item.update(locked=False, active=True)
            elif kind == "pump":
                assert "battery" in self.inventory
                self.inventory.remove("battery")
                item["active"] = True
            elif kind == "valve":
                assert next(o for o in self.objects if o["kind"] == "pump")["active"]
                item["active"] = True
            elif kind == "delivery":
                assert "parcel" in self.inventory
                self.inventory.remove("parcel")
                item["active"] = True
            else:
                raise AssertionError(d)
        else:
            raise AssertionError(("unexpected action", d))
        task = (next(o for o in self.objects if o["kind"] == "delivery")["active"]
                if self.mission == "delivery" else
                all(next(o for o in self.objects if o["kind"] == k)["active"] for k in ("pump", "valve")))
        self.completed = task and abs(self.pos[0] - 2) + abs(self.pos[1] - 2) <= 1
        self.outcome = action_outcome(d, self.pos)
        if action == "move" and self.pos != (d["x"], d["z"]):
            self.outcome.update(status="blocked", distanceMm=2000)
        self.seq += 1


class BridgeTests(unittest.TestCase):
    def setUp(self):
        # An assertion, rather than an import error, establishes the fail-first baseline.
        self.assertTrue((BRIDGE / "station_agent.py").is_file(), "station adapter is not implemented")
        self.api = importlib.import_module("station_agent")
        self.temp = tempfile.TemporaryDirectory(prefix="echo-station-test-")
        self.addCleanup(self.temp.cleanup)
        self.service = self.api.BridgeService(artifact_root=Path(self.temp.name))
        self.addCleanup(self.service.close)

    def decide(self, value, service=None):
        return json.loads((service or self.service).process(encoded(value)))

    def rejected(self, raw, status=400, service=None):
        with self.assertRaises(self.api.ProtocolError) as raised:
            (service or self.service).process(raw)
        self.assertEqual(raised.exception.status, status)

    def test_real_plan_supplies_one_cell_and_public_prior(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        original = self.api.HypothesisAgent.plan
        calls = []

        def tracked(actor, *args, **kwargs):
            result = original(actor, *args, **kwargs)
            calls.append((args, kwargs, result))
            return result

        with patch.object(self.api.HypothesisAgent, "plan", tracked):
            d = self.decide(o)
        self.assertEqual(d["action"], "move")
        self.assertEqual((d["x"], d["z"]), (3, 2))
        self.assertTrue(calls)
        self.assertEqual(calls[-1][1]["effect_prior"], ((0, 1), (0, -1), (1, 0), (-1, 0)))
        self.assertEqual(calls[-1][2]["positions"][1], [d["z"], d["x"]])
        self.assertEqual(d["plan"][0], {"x": 3, "z": 2})
        self.assertIn("declared", " ".join(d["hypotheses"]).lower())
        self.assertGreater(d["budgetUsed"], 0)
        self.assertEqual(d["budgetUsed"] + d["budgetRemaining"], 1_000_000)
        self.assertEqual(set(d), set("schema session seq action x z objectId reason prediction hypotheses evidence budgetUsed budgetRemaining knownCells plan source journal phase".split()))

    def test_partial_map_does_not_report_unknown_as_blocked(self):
        o = observation(cells=[dict(x=2, z=2, blocked=False)], objects=[])
        d = self.decide(o)
        self.assertEqual(d["knownCells"], o["cells"])
        self.assertEqual(d["action"], "wait")
        rows = [json.loads(line) for line in Path(d["journal"]).read_text().splitlines()]
        observed = next(r["observation"] for r in rows if r["kind"] == "station_observation")
        self.assertEqual(observed, o)
        self.assertNotIn("walls", json.dumps(rows))
        from echo5.trace import verify_journal
        self.assertEqual(verify_journal(d["journal"])["events"], len(rows))

    def test_unknown_compatibility_requires_inspection_even_if_true(self):
        decisions = []
        for index, compatibility in enumerate((False, True)):
            o = observation(session=f"unknown-battery-{index}", mission="irrigation",
                            inventory=["key"], objects=[obj("cell", "battery", 3, 2,
                                                          compatible=compatibility)])
            decisions.append(self.decide(o))
        for d in decisions:
            self.assertEqual((d["action"], d["objectId"]), ("inspect", "cell"))
        self.assertEqual(decisions[0]["reason"], decisions[1]["reason"])

    def test_inspected_incompatible_battery_is_not_collected(self):
        o = observation(mission="irrigation", inventory=["key"],
                        objects=[obj("bad", "battery", 3, 2, inspected=True),
                                 obj("good", "battery", 2, 3, inspected=True, compatible=True)])
        d = self.decide(o)
        self.assertEqual((d["action"], d["objectId"]), ("interact", "good"))

    def test_known_blocked_route_is_avoided(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        next(c for c in o["cells"] if (c["x"], c["z"]) == (3, 2))["blocked"] = True
        d = self.decide(o)
        self.assertEqual(d["action"], "move")
        self.assertNotIn({"x": 3, "z": 2}, d["plan"])
        self.assertNotIn({"x": 5, "z": 2}, d["plan"])
        for point in d["plan"]:
            self.assertIn(dict(point, blocked=False), d["knownCells"])

    def test_new_visible_obstacle_replans_without_claiming_motor_learning(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        d = self.decide(o)
        o["seq"] += 1
        o["lastResult"] = "blocked"
        o["outcome"] = action_outcome(d, (o["x"], o["z"]), status="blocked", distance_mm=2000)
        next(c for c in o["cells"] if (c["x"], c["z"]) == (d["x"], d["z"]))["blocked"] = True
        changed = self.decide(o)
        self.assertEqual(changed["action"], "move")
        self.assertNotEqual((changed["x"], changed["z"]), (d["x"], d["z"]))
        self.assertEqual(self.service.sessions[o["session"]].agent.core.evidence, [])

    def test_unmodeled_move_failure_retries_once_then_stops(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        first = self.decide(o)
        o.update(seq=1, lastResult="actuator failed")
        o["outcome"] = action_outcome(first, (o["x"], o["z"]), status="blocked", distance_mm=2000)
        retry = self.decide(o)
        self.assertEqual((retry["action"], retry["x"], retry["z"]),
                         (first["action"], first["x"], first["z"]))
        o["seq"] = 2
        o["outcome"] = action_outcome(retry, (o["x"], o["z"]), status="blocked", distance_mm=2000)
        stopped = self.decide(o)
        self.assertEqual((stopped["action"], stopped["phase"]), ("wait", "actuator_failure"))

    def test_low_logical_budget_stops_and_is_independent(self):
        small = self.api.BridgeService(artifact_root=Path(self.temp.name), budget_limit=4)
        self.addCleanup(small.close)
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        d = self.decide(o, small)
        self.assertEqual((d["action"], d["phase"]), ("wait", "budget_exhausted"))
        self.assertGreaterEqual(d["budgetRemaining"], 0)
        normal = self.decide(o)
        self.assertEqual(normal["action"], "move")
        o["session"] = "independent-session"
        other = self.decide(o)
        self.assertEqual(normal["budgetUsed"], other["budgetUsed"])
        self.assertNotEqual(normal["journal"], other["journal"])

    def test_low_battery_recharges_then_resumes(self):
        o = observation(battery=20, objects=[obj("home", "charger", 2, 2),
                                           obj("parcel", "parcel", 5, 2)])
        d = self.decide(o)
        self.assertEqual((d["action"], d["objectId"]), ("interact", "home"))
        o.update(seq=1, battery=100, lastResult="recharged")
        o["outcome"] = action_outcome(d, (o["x"], o["z"]))
        resumed = self.decide(o)
        self.assertEqual(resumed["action"], "move")

    def test_unaffordable_charger_route_stops(self):
        o = observation(x=5, z=5, battery=1)
        d = self.decide(o)
        self.assertEqual(d["action"], "wait")
        self.assertEqual(d["phase"], "energy_blocked")

    def test_completed_observation_is_authority(self):
        d = self.decide(observation(completed=True))
        self.assertEqual((d["action"], d["phase"]), ("done", "completed"))

    def test_inventory_full_does_not_pick_up_another_item(self):
        d = self.decide(observation(mission="irrigation", inventory=["key", "parcel", "parcel"],
                                   objects=[obj("battery", "battery", 3, 2, inspected=True, compatible=True)]))
        self.assertNotEqual((d["action"], d["objectId"]), ("interact", "battery"))

    def test_latest_retry_is_identical_and_has_no_side_effects(self):
        raw = encoded(observation())
        first = self.service.process(raw)
        path = Path(json.loads(first)["journal"])
        log = path.read_bytes()
        self.assertEqual(first, self.service.process(raw))
        self.assertEqual(log, path.read_bytes())
        o = observation(seq=1, completed=True)
        self.decide(o)
        self.rejected(raw, 409)

    def test_changed_retry_sequence_gap_and_changed_mission_are_rejected(self):
        self.decide(observation())
        for change in ({"battery": 99}, {"seq": 2}, {"seq": 1, "mission": "detour"}):
            with self.subTest(change=change):
                self.rejected(encoded(observation(**change)), 409)
        self.rejected(json.dumps(observation(), indent=2).encode(), 409)

    def test_session_and_request_caps_preserve_retry(self):
        small = self.api.BridgeService(artifact_root=Path(self.temp.name), max_sessions=1, max_requests=2)
        self.addCleanup(small.close)
        first = observation(completed=True)
        self.decide(first, small)
        self.rejected(encoded(observation(session="another")), 503, small)
        first["seq"] = 1
        response = small.process(encoded(first))
        self.assertEqual(response, small.process(encoded(first)))
        first["seq"] = 2
        self.rejected(encoded(first), 429, small)

    def test_400_unique_requests_limit(self):
        for seq in range(400):
            self.decide(observation(seq=seq, completed=True))
        self.rejected(encoded(observation(seq=400, completed=True)), 429)

    def test_strict_json_and_schema_validation(self):
        values = [b"null", b"[]", b"{", b"\xff", b'{"seq":0,"seq":1}',
                  encoded(observation()).replace(b'"battery":100', b'"battery":NaN'),
                  b"[" * 1100 + b"]" * 1100]
        changes = [dict(seq=True), dict(seq=-1), dict(seq=1.0), dict(battery=101),
                   dict(width=16), dict(height=14), dict(x=15), dict(z=-1),
                   dict(inventory=["secret"]), dict(inventory=["key"] * 4),
                   dict(session="../escape"), dict(session=""), dict(mission="hidden"),
                   dict(privateWorld={}), dict(completed=1), dict(cells=None),
                   dict(cells=[dict(x=2, z=2, blocked=0)]),
                   dict(cells=[dict(x=2, z=2, blocked=False)] * 2),
                   dict(objects=[obj("x", "secret", 3, 2)]),
                   dict(objects=[obj("x", "key", 3, 2)] * 2),
                   dict(lastResult="x" * 4097)]
        values.extend(encoded(observation(**change)) for change in changes)
        missing = observation()
        del missing["lastResult"]
        values.append(encoded(missing))
        bad_bool = observation(objects=[obj("battery", "battery", 3, 2)])
        bad_bool["objects"][0]["inspected"] = 1
        values.append(encoded(bad_bool))
        for raw in values:
            with self.subTest(raw=raw[:80]):
                self.rejected(raw)
        self.rejected(b" " * (1_048_576 + 1), 413)
        self.assertEqual(len(self.service.sessions), 0)

    def test_changed_object_identity_and_unbounded_history_are_rejected(self):
        o = observation(objects=[obj("stable", "key", 3, 2)])
        self.decide(o)
        o.update(seq=1, objects=[obj("stable", "pump", 3, 2)])
        self.rejected(encoded(o), 409)

    def journal_events(self, decision, kind):
        return [row for row in map(json.loads, Path(decision["journal"]).read_text().splitlines())
                if row["kind"] == kind]

    def test_outcome_is_required_and_strictly_validated(self):
        missing = observation()
        del missing["outcome"]
        self.rejected(encoded(missing))
        changes = [None, {}, dict(empty_outcome(), present=1), dict(empty_outcome(), seq=0),
                   dict(empty_outcome(), status="arrived"), dict(empty_outcome(), distanceMm=True),
                   dict(empty_outcome(), extra="private")]
        claim = action_outcome(dict(session="manual", seq=7, action="move", objectId="",
                                    x=3, z=2), (2, 2), status="blocked", distance_mm=1800)
        changes += [dict(claim, seq=-1), dict(claim, session=""), dict(claim, status="applied"),
                    dict(claim, action="teleport"), dict(claim, actualX=15),
                    dict(claim, distanceMm=-1), dict(claim, distanceMm=1.5),
                    dict(claim, action="inspect", status="applied", distanceMm=1)]
        for outcome in changes:
            with self.subTest(outcome=outcome):
                self.rejected(encoded(observation(outcome=outcome)))
        self.assertEqual(len(self.service.sessions), 0)

    def test_initial_foreign_manual_outcome_is_not_credited(self):
        claim = action_outcome(dict(session="manual", seq=17, action="interact", objectId="home",
                                    x=2, z=2), (2, 2))
        d = self.decide(observation(outcome=claim, objects=[obj("parcel", "parcel", 5, 2)]))
        self.assertEqual(d["action"], "move")
        self.assertEqual(self.journal_events(d, "station_consequence"), [])

    def test_rounded_midpoint_and_failed_status_never_confirm_arrival(self):
        for index, (status, distance) in enumerate((("arrived", 201), ("arrived", 900),
                                                   ("blocked", 900), ("blocked", 0),
                                                   ("cancelled", 0), ("rejected", 0))):
            with self.subTest(status=status, distance=distance):
                o = observation(session=f"midpoint-{index}", objects=[obj("parcel", "parcel", 5, 2)])
                d = self.decide(o)
                o.update(seq=1, x=d["x"], z=d["z"],
                         outcome=action_outcome(d, (d["x"], d["z"]), status=status, distance_mm=distance))
                after = self.decide(o)
                event = self.journal_events(d, "station_consequence")[-1]
                self.assertTrue(event["matched"])
                self.assertFalse(event["confirmed"])
                self.assertEqual(event["resolution"], "failed")
                self.assertEqual(after["action"], "wait")

    def test_arrival_at_200mm_requires_matching_receipt_and_pose(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        d = self.decide(o)
        self.assertEqual(d["objectId"], "parcel")
        o.update(seq=1, x=d["x"], z=d["z"],
                 outcome=action_outcome(d, (d["x"], d["z"]), distance_mm=200))
        self.decide(o)
        event = self.journal_events(d, "station_consequence")[-1]
        self.assertTrue(event["matched"])
        self.assertTrue(event["confirmed"])
        self.assertEqual(event["outcome"], o["outcome"])

    def test_missing_or_mismatched_receipt_is_unresolved_not_a_new_action(self):
        changes = [None, {"session": "manual"}, {"seq": 42},
                   {"action": "inspect", "status": "applied"}, {"objectId": ""},
                   {"targetX": 4}, {"targetZ": 3}, {"actualX": 4}, {"actualZ": 3}]
        for index, change in enumerate(changes):
            with self.subTest(change=change):
                o = observation(session=f"mismatch-{index}", objects=[obj("parcel", "parcel", 5, 2)])
                d = self.decide(o)
                claim = action_outcome(d, (d["x"], d["z"]))
                if change is None:
                    claim = empty_outcome()
                else:
                    claim.update(change)
                o.update(seq=1, x=d["x"], z=d["z"], outcome=claim)
                after = self.decide(o)
                self.assertEqual((after["action"], after["phase"]), ("wait", "outcome_unresolved"))
                event = self.journal_events(d, "station_consequence")[-1]
                self.assertFalse(event["matched"])
                self.assertFalse(event["confirmed"])
                self.assertEqual(event["resolution"], "unresolved")

    def test_interaction_requires_applied_and_current_public_facts(self):
        for index, (status, inventory, expected) in enumerate((("applied", ["key"], True),
                                                               ("rejected", ["key"], False),
                                                               ("applied", [], False))):
            with self.subTest(status=status, inventory=inventory):
                o = observation(session=f"interaction-{index}", mission="irrigation",
                                objects=[obj("key", "key", 3, 2)])
                d = self.decide(o)
                self.assertEqual((d["action"], d["objectId"]), ("interact", "key"))
                o.update(seq=1, inventory=inventory, objects=[],
                         outcome=action_outcome(d, (2, 2), status=status))
                self.decide(o)
                event = self.journal_events(d, "station_consequence")[-1]
                self.assertTrue(event["matched"])
                self.assertEqual(event["confirmed"], expected)

    def test_inspection_requires_current_visible_inspection_fact(self):
        o = observation(mission="irrigation", inventory=["key"],
                        objects=[obj("cell", "battery", 3, 2)])
        d = self.decide(o)
        self.assertEqual(d["action"], "inspect")
        o.update(seq=1, objects=[], outcome=action_outcome(d, (2, 2)))
        self.decide(o)
        self.assertFalse(self.journal_events(d, "station_consequence")[-1]["confirmed"])

    def test_finish_records_cancelled_outcome_without_choosing_or_planning(self):
        for seq in (0, 1):
            with self.subTest(seq=seq):
                o = observation(session=f"finish-cancel-{seq}", objects=[obj("parcel", "parcel", 5, 2)])
                d = self.decide(o)
                before = Path(d["journal"]).read_text().splitlines()
                agent = self.service.sessions[o["session"]].agent
                used = agent.budget.used
                o.update(seq=seq, outcome=action_outcome(d, (2, 2), status="cancelled", distance_mm=1200))
                with patch.object(agent, "_choose", side_effect=AssertionError("finish chose an action")), \
                        patch.object(self.api.HypothesisAgent, "plan", side_effect=AssertionError("finish planned")):
                    response = json.loads(self.service.finish(encoded(o)))
                self.assertEqual(response["status"], "closed")
                self.assertEqual(response["resolution"], "failed")
                self.assertFalse(response["confirmed"])
                self.assertNotIn("action", response)
                self.assertEqual(agent.budget.used, used)
                extra = [json.loads(line) for line in Path(d["journal"]).read_text().splitlines()[len(before):]]
                self.assertEqual([row["kind"] for row in extra],
                                 ["station_observation", "station_consequence", "station_finish"])
                self.assertEqual(extra[1]["outcome"]["status"], "cancelled")
                self.assertTrue(extra[1]["matched"])
                self.assertFalse(extra[1]["confirmed"])
                from echo5.trace import verify_journal
                self.assertEqual(verify_journal(d["journal"])["events"], len(before) + 3)

    def test_finish_is_exact_byte_idempotent_and_closed_session_cannot_decide(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        d = self.decide(o)
        proposal_request = encoded(o)
        o.update(seq=1, x=d["x"], z=d["z"], outcome=action_outcome(d, (d["x"], d["z"])))
        raw = encoded(o)
        first = self.service.finish(raw)
        self.assertTrue(json.loads(first)["confirmed"])
        journal = Path(d["journal"]).read_bytes()
        self.assertEqual(self.service.finish(raw), first)
        self.assertEqual(Path(d["journal"]).read_bytes(), journal)
        with self.assertRaises(self.api.ProtocolError) as raised:
            self.service.finish(json.dumps(o, indent=2).encode())
        self.assertEqual(raised.exception.status, 409)
        self.rejected(proposal_request, 409)
        self.rejected(encoded(dict(o, seq=2)), 409)

    def test_finish_missing_receipt_closes_pending_without_forging_execution(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        d = self.decide(o)
        result = json.loads(self.service.finish(encoded(o)))
        self.assertEqual(result["resolution"], "unresolved")
        self.assertFalse(result["confirmed"])
        self.assertTrue(self.journal_events(d, "station_finish")[-1]["pending"])

    def test_finish_does_not_credit_stale_outcome_for_latest_proposal(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        first = self.decide(o)
        o.update(seq=1, x=first["x"], z=first["z"],
                 outcome=action_outcome(first, (first["x"], first["z"])))
        second = self.decide(o)
        self.assertEqual(second["seq"], 1)
        result = json.loads(self.service.finish(encoded(o)))
        self.assertEqual(result["resolution"], "unresolved")
        events = self.journal_events(first, "station_consequence")
        self.assertEqual(len(events), 2)
        self.assertTrue(events[0]["confirmed"])
        self.assertFalse(events[1]["confirmed"])
        self.assertFalse(events[1]["matched"])
        self.assertEqual(events[1]["previous_seq"], 1)

    def test_finish_validates_existing_session_sequence_and_scenario(self):
        with self.assertRaises(self.api.ProtocolError) as raised:
            self.service.finish(encoded(observation()))
        self.assertEqual(raised.exception.status, 404)
        self.assertEqual(list(Path(self.temp.name).iterdir()), [])
        o = observation()
        d = self.decide(o)
        journal = Path(d["journal"]).read_bytes()
        for change in ({"seq": 2}, {"mission": "detour"}, {"width": 14}):
            with self.subTest(change=change):
                with self.assertRaises(self.api.ProtocolError) as raised:
                    self.service.finish(encoded(dict(o, **change)))
                self.assertEqual(raised.exception.status, 409)
                self.assertEqual(Path(d["journal"]).read_bytes(), journal)

    def test_finish_still_works_at_request_and_logical_budget_limits(self):
        service = self.api.BridgeService(artifact_root=Path(self.temp.name), max_requests=1, budget_limit=0)
        self.addCleanup(service.close)
        o = observation()
        d = self.decide(o, service)
        self.assertEqual(d["phase"], "budget_exhausted")
        result = json.loads(service.finish(encoded(o)))
        self.assertEqual((result["status"], result["resolution"]), ("closed", "no_action"))

    def test_exact_unity_world_occlusion_pickup_disappearance_and_detour(self):
        for mission in ("irrigation", "delivery", "detour"):
            for variant in (0, 1):
                with self.subTest(mission=mission, variant=variant):
                    env = UnityWorld(mission, variant)
                    seen = {}
                    detour_attempts, detour_arrivals = [], []
                    for turn in range(400):
                        o = env.observation()
                        seen.update({(c["x"], c["z"]): c["blocked"] for c in o["cells"]})
                        d = self.decide(o)
                        self.assertEqual({(c["x"], c["z"]): c["blocked"] for c in d["knownCells"]}, seen)
                        self.assertTrue(all(isinstance(d[k], list) for k in ("plan", "knownCells", "hypotheses", "evidence")))
                        if d["action"] == "done":
                            self.assertTrue(env.completed)
                            break
                        was_blocked = env.detour
                        if was_blocked:
                            self.assertNotIn({"x": 5, "z": 9}, d["plan"])
                            if d["action"] == "move":
                                detour_attempts.append((d["x"], d["z"]))
                                self.assertNotEqual(detour_attempts[-1], (5, 9))
                        env.step(d)
                        if was_blocked and d["action"] == "move":
                            detour_arrivals.append(env.pos)
                            self.assertNotEqual(env.pos, (5, 9))
                        if not was_blocked and env.detour:
                            self.assertEqual((o["x"], o["z"]), (3, 9))
                            self.assertEqual(env.pos, (4, 9))
                            self.assertIn(dict(x=5, z=9, blocked=True), env.observation()["cells"])
                    else:
                        self.fail(f"Unity {mission}/{variant} exceeded 400 requests")
                    if variant:
                        self.assertGreater(env.recharges, 0)
                    if mission != "delivery":
                        self.assertIn("key", env.inventory)
                        self.assertIn(("interact", "water-pump"), env.actions)
                        self.assertIn(("interact", "water-valve"), env.actions)
                    if mission == "detour":
                        self.assertTrue(env.detour)
                        alternate = {(5, 3), (7, 6)}
                        self.assertTrue(alternate.intersection(detour_attempts), "No alternate crossing attempted after blockage")
                        self.assertTrue(alternate.intersection(detour_arrivals), "No alternate crossing reached after blockage")
                    self.assertLess(d["budgetUsed"], 1_000_000)
                    print(f"UNITY {mission}/{variant}: {turn + 1} requests, {d['budgetUsed']} logical units, "
                          f"{env.recharges} recharges", flush=True)

    def test_all_missions_finish_with_partial_observations(self):
        for mission in ("irrigation", "delivery", "detour"):
            for variant in (0, 1):
                with self.subTest(mission=mission, variant=variant):
                    env = PublicWorld(mission, variant)
                    seen = {}
                    for turn in range(400):
                        o = env.observation()
                        seen.update({(c["x"], c["z"]): c["blocked"] for c in o["cells"]})
                        d = self.decide(o)
                        self.assertEqual({(c["x"], c["z"]): c["blocked"] for c in d["knownCells"]}, seen)
                        self.assertEqual((d["session"], d["seq"]), (env.session, env.seq))
                        if d["action"] == "done":
                            self.assertTrue(env.completed)
                            break
                        env.step(d)
                    else:
                        self.fail(f"{mission}/{variant} did not finish")
                    self.assertEqual(env.pos, (2, 2))
                    self.assertGreater(env.recharges, 0)
                    self.assertLess(d["budgetUsed"], 1_000_000)
                    if mission != "delivery":
                        self.assertIn(("interact", "key"), env.actions)
                        self.assertIn(("interact", "gate"), env.actions)
                        self.assertIn(("interact", "pump"), env.actions)
                        self.assertIn(("interact", "valve"), env.actions)
                    print(f"MISSION {mission}/{variant}: {turn + 1} requests, "
                          f"{d['budgetUsed']} logical units, {env.recharges} recharges", flush=True)


class HTTPTests(BridgeTests):
    # Only HTTP methods here are collected; avoid inheriting the full agent suite twice.
    def setUp(self):
        super().setUp()
        self.assertTrue((BRIDGE / "server.py").is_file(), "HTTP server is not implemented")
        self.http_api = importlib.import_module("server")
        self.httpd = self.http_api.make_server(port=0, service=self.service)
        self.port = self.httpd.server_address[1]
        self.thread = threading.Thread(target=self.httpd.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
        self.thread.start()
        self.addCleanup(self.stop_server)

    def stop_server(self):
        self.httpd.shutdown()
        self.httpd.server_close()
        self.thread.join(2)

    def request(self, method, path, body=None, headers=None):
        c = http.client.HTTPConnection("127.0.0.1", self.port, timeout=3)
        try:
            c.request(method, path, body, headers or {})
            r = c.getresponse()
            return r.status, dict(r.getheaders()), r.read()
        finally:
            c.close()

    def test_http_health_has_no_session_state_and_post_roundtrip(self):
        raw = encoded(observation())
        status, headers, first = self.request("POST", "/decide", raw, {"Content-Type": "application/json"})
        self.assertEqual(status, 200)
        self.assertEqual(self.request("POST", "/decide", raw, {"Content-Type": "application/json"})[2], first)
        status, headers, health = self.request("GET", "/health")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(health), {"schema": "echo.station.health.v1", "status": "ok"})
        self.assertFalse(any(k.lower().startswith("access-control-") for k in headers))
        for path in ("/decide", "/finish", "/state", "/thought.jsonl", "/health?session=x"):
            self.assertEqual(self.request("GET", path)[0], 404)

    def test_http_finish_uses_same_guards_and_exact_retry_cache(self):
        o = observation(objects=[obj("parcel", "parcel", 5, 2)])
        headers = {"Content-Type": "application/json"}
        self.assertEqual(self.request("POST", "/finish", encoded(o), headers)[0], 404)
        status, _, raw = self.request("POST", "/decide", encoded(o), headers)
        self.assertEqual(status, 200)
        d = json.loads(raw)
        o.update(seq=1, outcome=action_outcome(d, (2, 2), status="cancelled", distance_mm=1500))
        raw = encoded(o)
        self.assertEqual(self.request("POST", "/finish", raw, dict(headers, Origin="https://evil.example"))[0], 403)
        self.assertEqual(self.request("POST", "/finish", raw, {"Content-Type": "text/plain"})[0], 415)
        self.assertEqual(self.request("POST", "/finish", b"", {**headers, "Content-Length": "1048577"})[0], 413)
        status, _, response = self.request("POST", "/finish", raw, headers)
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(response)["status"], "closed")
        self.assertEqual(self.request("POST", "/finish", raw, headers)[2], response)
        self.assertEqual(self.request("POST", "/finish", raw + b" ", headers)[0], 409)
        self.assertEqual(self.request("POST", "/decide", raw, headers)[0], 409)

    def test_http_rejects_browser_origins_hosts_and_wrong_content_type(self):
        raw = encoded(observation())
        for headers in ({"Origin": "https://evil.example", "Content-Type": "application/json"},
                        {"Host": "evil.example", "Content-Type": "application/json"},
                        {"Origin": "null", "Content-Type": "application/json"},
                        {"Sec-Fetch-Site": "cross-site", "Content-Type": "application/json"}):
            self.assertEqual(self.request("POST", "/decide", raw, headers)[0], 403)
        self.assertEqual(self.request("POST", "/decide", raw, {"Content-Type": "text/plain"})[0], 415)
        self.assertEqual(self.request("OPTIONS", "/decide")[0], 405)

    def test_http_rejects_ambiguous_or_oversized_framing(self):
        for fields, expected in (("Content-Length: 1048577\r\n", 413),
                                 ("Content-Length: -1\r\n", 400),
                                 ("Content-Length: 0\r\nContent-Length: 1\r\n", 400),
                                 ("Transfer-Encoding: chunked\r\n", 400), ("", 411)):
            with socket.create_connection(("127.0.0.1", self.port), timeout=3) as c:
                request = (f"POST /decide HTTP/1.1\r\nHost: 127.0.0.1:{self.port}\r\n"
                           f"Content-Type: application/json\r\n{fields}\r\n")
                c.sendall(request.encode())
                self.assertIn(f" {expected} ".encode(), c.recv(4096).split(b"\r\n")[0])


# unittest inheritance would duplicate the core tests; keep one HTTP-only subclass.
for _name in tuple(vars(BridgeTests)):
    if _name.startswith("test_"):
        setattr(HTTPTests, _name, None)


if __name__ == "__main__":
    unittest.main(verbosity=2)
