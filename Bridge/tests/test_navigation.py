"""CPU contract tests. Test geometry/effect mappings never enter the backend."""

import copy
import http.client
import importlib
import importlib.util
import itertools
import json
import math
from pathlib import Path
import select
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def encoded(value):
    return json.dumps(value, separators=(",", ":"), allow_nan=False).encode()


def empty_outcome():
    return dict(present=False, session="", seq=-1, action="", command=-1,
                status="", fromX=0, fromZ=0, x=0, z=0, distanceMm=0)


def observation(**changes):
    value = dict(schema="echo.navigation.observation.v1", session="nav-test", seq=0,
                 world="public-world-id", profile="cold", width=7, height=7,
                 x=3, z=3, goalX=6, goalZ=6, yaw=0, completed=False,
                 rays=[], outcome=empty_outcome())
    value.update(changes)
    return value


def receipt(decision, before, after, status=None, distance=0):
    return dict(present=True, session=decision["session"], seq=decision["seq"],
                action=decision["action"], command=decision["command"],
                status=status or ("scanned" if decision["action"] == "scan" else "arrived"),
                fromX=before[0], fromZ=before[1], x=after[0], z=after[1],
                distanceMm=distance)


class CameraWorld:
    """Only depth rays and receipts cross the boundary; no dataset imports."""

    def __init__(self, effects=((0, 1), (-1, 0), (0, -1), (1, 0)),
                 *, profile="learn", session="acquire", world="fixture", walls=()):
        self.effects = effects
        self.walls = set(walls)
        self.obs = observation(profile=profile, session=session, world=world)
        self.moves = []

    def capture(self):
        obs = copy.deepcopy(self.obs)
        sx, sz = obs["x"] * 2000, obs["z"] * 2000
        rays = []
        for offset in range(-48, 49, 8):
            angle = math.radians(obs["yaw"] + offset)
            hit = False
            for distance in range(25, 8025, 25):
                ex = round(sx + math.sin(angle) * distance)
                ez = round(sz + math.cos(angle) * distance)
                cell = ((ex + 1000) // 2000, (ez + 1000) // 2000)
                if (not 0 <= cell[0] < 7 or not 0 <= cell[1] < 7
                        or cell in self.walls):
                    hit = True
                    break
            rays.append(dict(sx=sx, sz=sz, ex=ex, ez=ez, hit=hit))
        obs["rays"] = rays
        return obs

    def step(self, decision):
        before = self.obs["x"], self.obs["z"]
        after = before
        if decision["action"] == "scan":
            self.obs["yaw"] = decision["yaw"]
            status, distance = "scanned", 0
        elif decision["action"] == "move":
            dx, dz = self.effects[decision["command"]]
            target = before[0] + dx, before[1] + dz
            blocked = target in self.walls or not (0 <= target[0] < 7 and 0 <= target[1] < 7)
            after = before if blocked else target
            status, distance = ("blocked", 2000) if blocked else ("arrived", 0)
            self.moves.append((before, decision["command"], after))
        else:
            raise AssertionError("fixture cannot execute " + decision["action"])
        self.obs.update(seq=self.obs["seq"] + 1, x=after[0], z=after[1],
                        completed=after == (self.obs["goalX"], self.obs["goalZ"]),
                        outcome=receipt(decision, before, after, status, distance))


class NavigationTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(importlib.util.find_spec("navigation"),
                             "navigation backend has not been implemented")
        self.api = importlib.import_module("navigation")
        self.tmp = tempfile.TemporaryDirectory(prefix="echo-navigation-tests-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.model = self.root / "motor.json"
        self.service = self.api.NavigationService(self.root / "runs", self.model)
        self.addCleanup(self.service.close)

    def decide(self, obs, service=None):
        return json.loads((service or self.service).process(encoded(obs)))

    def reject(self, raw, status=400, service=None, finish=False):
        target = service or self.service
        with self.assertRaises(self.api.ProtocolError) as raised:
            (target.finish if finish else target.process)(raw)
        self.assertEqual(raised.exception.status, status)

    def events(self, decision, kind=None):
        events = [json.loads(line) for line in Path(decision["journal"]).read_text().splitlines()]
        return [event for event in events if kind is None or event["kind"] == kind]

    def train(self, effects=None, *, session="acquire", world="acquisition", service=None):
        fixture = CameraWorld(**({"effects": effects} if effects else {}), session=session, world=world)
        target = service or self.service
        for _ in range(160):
            obs = fixture.capture()
            decision = self.decide(obs, target)
            if decision["action"] == "done":
                self.assertTrue(obs["completed"])
                self.assertEqual(decision["learnedActions"], 4)
                result = json.loads(target.finish(encoded(obs)))
                self.assertEqual(result["status"], "closed")
                return fixture, decision
            self.assertIn(decision["action"], ("move", "scan"), decision)
            fixture.step(decision)
        self.fail("acquisition/navigation exceeded fixture budget")

    def test_strict_schema_rejects_privileged_fields_and_wrong_json_types(self):
        invalid = [dict(observation(), layout=[]), dict(observation(), effects=[]),
                   dict(observation(), x=True), dict(observation(), yaw=360),
                   dict(observation(), width=17), dict(observation(), profile="train"),
                   dict(observation(), session="../../other"), dict(observation(), world="../map"),
                   dict(observation(), completed=1), dict(observation(), rays=[{}]),
                   dict(observation(), outcome=dict(empty_outcome(), command=0)),
                   dict(observation(), rays=[dict(sx=0, sz=0, ex=12001, ez=0, hit=False)]),
                   dict(observation(), rays=[dict(sx=0, sz=0, ex=1, ez=0, hit=False)] * 66)]
        for value in invalid:
            with self.subTest(value=value):
                self.reject(encoded(value))
        self.reject(b'{"schema":1,"schema":2}')
        self.reject(b'{"x":NaN}')
        self.reject(b"[]")
        self.reject(b" " * (1048576 + 1), 413)
        self.assertEqual(self.service.sessions, {})
        self.assertFalse((self.root / "runs").exists())

    def test_initial_sequence_and_receipt_are_strict(self):
        self.reject(encoded(observation(seq=1)), 409)
        foreign = receipt(dict(session="foreign", seq=0, action="move", command=1), (3, 3), (3, 4))
        self.reject(encoded(observation(outcome=foreign)), 409)

    def test_exact_retry_is_byte_identical_and_does_not_add_work_or_journal(self):
        raw = encoded(observation())
        first = self.service.process(raw)
        journal = Path(json.loads(first)["journal"]).read_bytes()
        self.assertEqual(first, self.service.process(raw))
        self.assertEqual(journal, Path(json.loads(first)["journal"]).read_bytes())
        self.reject(json.dumps(observation(), indent=2).encode(), 409)
        self.reject(encoded(observation(seq=2)), 409)

    def test_session_identity_goal_and_profile_cannot_change(self):
        self.decide(observation())
        for update in ({"world": "other"}, {"profile": "learn"}, {"width": 8}, {"goalX": 5}):
            with self.subTest(update=update):
                self.reject(encoded(dict(observation(seq=1), **update)), 409)

    def test_ray_integration_uses_world_mm_and_preserves_unknown_and_occlusion(self):
        ray = dict(sx=6000, sz=6000, ex=9000, ez=6000, hit=True)
        decision = self.decide(observation(rays=[ray]))
        cells = {(c["x"], c["z"]): c["state"] for c in decision["cells"]}
        self.assertEqual(cells, {(3, 3): "free", (4, 3): "free", (5, 3): "occupied"})
        self.assertNotIn((6, 3), cells)
        self.assertNotIn((6, 6), cells)

    def test_negative_direction_hit_at_cell_face_uses_far_side(self):
        decision = self.decide(observation(rays=[dict(sx=6000, sz=6000, ex=3000, ez=6000, hit=True)]))
        cells = {(c["x"], c["z"]): c["state"] for c in decision["cells"]}
        self.assertEqual(cells, {(3, 3): "free", (2, 3): "free", (1, 3): "occupied"})

    def test_outside_endpoints_and_corner_crossings_do_not_invent_cells(self):
        decision = self.decide(observation(x=0, z=0, rays=[
            dict(sx=0, sz=0, ex=-5000, ez=0, hit=True),
            dict(sx=0, sz=0, ex=4000, ez=4000, hit=False)]))
        cells = {(c["x"], c["z"]): c["state"] for c in decision["cells"]}
        self.assertEqual(cells, {(0, 0): "free", (1, 1): "free", (2, 2): "free"})

    def test_hit_wins_over_free_in_same_frame_independent_of_ray_order(self):
        rays = [dict(sx=6000, sz=6000, ex=8000, ez=6000, hit=True),
                dict(sx=6000, sz=6000, ex=10000, ez=6000, hit=False)]
        a = self.decide(observation(session="a", rays=rays))
        b = self.decide(observation(session="b", rays=list(reversed(rays))))
        self.assertEqual(a["cells"], b["cells"])
        self.assertIn(dict(x=4, z=3, state="occupied"), a["cells"])

    def test_unknown_map_scans_and_empty_model_has_no_fake_support(self):
        decision = self.decide(observation())
        self.assertEqual(decision["action"], "scan")
        self.assertEqual(decision["command"], -1)
        self.assertEqual(decision["support"], [0, 0, 0, 0])
        self.assertEqual(decision["learnedActions"], 0)
        self.assertEqual(len(decision["hypotheses"]), 4)

    def test_learning_transfers_for_multiple_private_command_permutations(self):
        permutations = list(itertools.permutations(((1, 0), (0, -1), (-1, 0), (0, 1))))
        for index in (0, 7, 14, 23):
            with self.subTest(permutation=index):
                model = self.root / f"model-{index}.json"
                service = self.api.NavigationService(self.root / f"run-{index}", model)
                self.addCleanup(service.close)
                fixture, decision = self.train(permutations[index], service=service)
                saved = json.loads(model.read_text())
                checkpoint = saved["checkpoint"]
                self.assertEqual(decision["support"], checkpoint["payload"]["model"]["support"])
                self.assertEqual(checkpoint["payload"]["model"]["hypotheses"],
                                 [[[dz, dx]] for dx, dz in fixture.effects])
                budget = self.api.Budget(2_000_000)
                restored = self.api.HypothesisAgent.restore(checkpoint, budget=budget)
                self.assertEqual(restored.checkpoint(), checkpoint)
                self.assertTrue(saved["acquisition"])

    def test_projection_keeps_physical_coordinates_and_independent_tick_zero(self):
        _, decision = self.train()
        events = self.events(decision, "navigation_calibration")
        learned = [event for event in events if event["accepted"]]
        self.assertGreaterEqual(len(learned), 8)
        for event in learned:
            physical, before, after = event["outcome"], event["before"], event["after"]
            self.assertEqual((before["width"], before["height"], before["walls"]), (16, 16, []))
            self.assertEqual((before["tick"], after["tick"]), (0, 1))
            self.assertEqual(before["position"], [physical["fromZ"], physical["fromX"]])
            self.assertEqual(after["position"], [physical["z"], physical["x"]])
            self.assertEqual((physical["status"], physical["distanceMm"]), ("arrived", 0))
        from echo5.trace import verify_journal
        self.assertEqual(verify_journal(decision["journal"])["events"], len(self.events(decision)))

    def test_core_duplicates_add_no_support_and_do_not_fail_session(self):
        self.train()
        saved = json.loads(self.model.read_text())["checkpoint"]
        sample = saved["payload"]["evidence"][0]
        core = self.api.HypothesisAgent.restore(saved, budget=self.api.Budget(2_000_000))
        recorder = self.api.Recorder(self.root / "duplicate.jsonl")
        self.addCleanup(recorder.close)
        agent = self.api.NavigationAgent(core=core, budget=self.api.Budget(2_000_000),
                                        recorder=recorder, journal_path=self.root / "duplicate.jsonl")
        before = observation(profile="learn", x=sample["before"]["position"][1],
                             z=sample["before"]["position"][0])
        decision = dict(session=before["session"], seq=0, action="move", command=sample["action"], yaw=0)
        agent.last_observation, agent.last_decision = before, decision
        after = observation(profile="learn", seq=1, x=sample["after"]["position"][1],
                            z=sample["after"]["position"][0])
        after["outcome"] = receipt(decision, (before["x"], before["z"]), (after["x"], after["z"]))
        result = agent.decide(self.api.validate_observation(after))
        self.assertEqual(result["support"], saved["payload"]["model"]["support"])
        self.assertEqual(core.checkpoint(), saved)
        self.assertTrue(any(not event["accepted"] for event in self.events(result, "navigation_calibration")))

    def test_only_arrived_unit_motion_with_small_residual_is_learned(self):
        for index, (status, delta, residual, expected) in enumerate([
                ("arrived", (1, 0), 200, 1), ("arrived", (1, 0), 201, 0),
                ("arrived", (0, 0), 0, 0), ("arrived", (2, 0), 0, 0),
                ("arrived", (1, 1), 0, 0), ("blocked", (1, 0), 0, 0),
                ("cancelled", (1, 0), 0, 0), ("blocked", (0, 0), 2000, 0)]):
            recorder = self.api.Recorder(self.root / f"physical-{index}.jsonl")
            self.addCleanup(recorder.close)
            core = self.api.HypothesisAgent()
            agent = self.api.NavigationAgent(core=core, budget=self.api.Budget(2_000_000),
                                            recorder=recorder, journal_path=self.root / f"physical-{index}.jsonl")
            before = observation()
            previous = dict(session=before["session"], seq=0, action="move", command=2, yaw=0)
            agent.last_observation, agent.last_decision = before, previous
            after = observation(seq=1, x=3 + delta[0], z=3 + delta[1])
            after["outcome"] = receipt(previous, (3, 3), (after["x"], after["z"]), status, residual)
            result = agent.decide(self.api.validate_observation(after))
            self.assertEqual(sum(result["support"]), expected, (status, delta, residual))

    def test_missing_trained_model_fails_without_creating_session(self):
        for profile in ("trained", "no_memory", "remembered"):
            with self.subTest(profile=profile):
                self.reject(encoded(observation(profile=profile)), 409)
        self.assertEqual(self.service.sessions, {})

    def test_frozen_profiles_never_observe_and_plan_without_prior(self):
        fixture, acquired = self.train()
        saved = self.model.read_bytes()
        original_plan = self.api.HypothesisAgent.plan
        calls = []

        def checked_plan(core, obs, **kwargs):
            self.assertNotIn("effect_prior", kwargs)
            calls.append(obs)
            return original_plan(core, obs, **kwargs)

        for profile in ("trained", "no_memory", "remembered"):
            with self.subTest(profile=profile), patch.object(self.api.HypothesisAgent, "plan", checked_plan):
                env = CameraWorld(fixture.effects, profile=profile, session=profile, world="novel")
                first = self.decide(env.capture())  # Restore replays evidence using real observe.
                with patch.object(self.api.HypothesisAgent, "observe", side_effect=AssertionError("frozen update")):
                    result = first
                    for _ in range(10):
                        if result["action"] in ("done", "wait"):
                            break
                        env.step(result)
                        result = self.decide(env.capture())
                        self.assertEqual(result["modelHash"], acquired["modelHash"])
                self.assertEqual(self.model.read_bytes(), saved)
        self.assertTrue(calls)

    def test_cold_ignores_saved_model_and_does_not_persist(self):
        self.train()
        saved = self.model.read_bytes()
        cold = CameraWorld(profile="cold", session="cold")
        result = self.decide(cold.capture())
        self.assertEqual(result["support"], [0] * 4)
        for _ in range(16):
            if result["action"] in ("done", "wait"):
                break
            cold.step(result)
            result = self.decide(cold.capture())
        self.assertGreater(sum(result["support"]), 0)
        self.service.finish(encoded(cold.capture()))
        self.assertEqual(self.model.read_bytes(), saved)

    def test_no_memory_discards_previous_ray_cells_but_preserves_model(self):
        self.train()
        obs = observation(profile="no_memory", session="forget", rays=[
            dict(sx=6000, sz=6000, ex=6000, ez=10000, hit=False)])
        first = self.decide(obs)
        self.assertIn(dict(x=3, z=5, state="free"), first["cells"])
        self.assertEqual(first["action"], "scan")
        obs.update(seq=1, yaw=first["yaw"], rays=[], outcome=receipt(first, (3, 3), (3, 3)))
        second = self.decide(obs)
        self.assertEqual(second["cells"], [dict(x=3, z=3, state="free")])
        self.assertEqual(second["modelHash"], first["modelHash"])

    def test_collision_infers_cell_separately_and_never_repeats_same_failed_edge(self):
        self.train()
        obs = observation(profile="trained", session="collision", goalX=5, goalZ=3,
                          rays=[dict(sx=6000, sz=6000, ex=10000, ez=6000, hit=False)])
        first = self.decide(obs)
        self.assertEqual(first["action"], "move")
        obs.update(seq=1, outcome=receipt(first, (3, 3), (3, 3), "blocked", 2000))
        second = self.decide(obs)
        self.assertIn(dict(x=4, z=3, state="inferred"), second["cells"])
        self.assertNotEqual((second["action"], second["command"]), ("move", first["command"]))
        self.assertEqual(first["modelHash"], second["modelHash"])
        if second["action"] == "scan":
            obs.update(seq=2, yaw=second["yaw"], outcome=receipt(second, (3, 3), (3, 3)))
            third = self.decide(obs)
            self.assertNotEqual((third["action"], third["command"]), ("move", first["command"]))

    def test_new_measurement_can_refute_inferred_obstacle(self):
        self.train()
        obs = observation(profile="trained", session="refute", goalX=5, goalZ=3,
                          rays=[dict(sx=6000, sz=6000, ex=10000, ez=6000, hit=False)])
        first = self.decide(obs)
        obs.update(seq=1, outcome=receipt(first, (3, 3), (3, 3), "blocked", 2000))
        second = self.decide(obs)
        self.assertEqual(second["action"], "scan")
        obs.update(seq=2, yaw=second["yaw"], outcome=receipt(second, (3, 3), (3, 3)),
                   rays=[dict(sx=6100, sz=6000, ex=11000, ez=6000, hit=False)])
        third = self.decide(obs)
        self.assertIn(dict(x=4, z=3, state="free"), third["cells"])
        self.assertEqual(third["action"], "move")

    def test_frozen_model_conflict_stops_without_learning_a_new_mapping(self):
        self.train()
        obs = observation(profile="trained", session="bad-model", goalX=5, goalZ=3,
                          rays=[dict(sx=6000, sz=6000, ex=10000, ez=6000, hit=False)])
        first = self.decide(obs)
        self.assertEqual(first["action"], "move")
        obs.update(seq=1, x=2, outcome=receipt(first, (3, 3), (2, 3)))
        second = self.decide(obs)
        self.assertEqual(second["action"], "wait")
        self.assertEqual(second["modelHash"], first["modelHash"])

    def test_finish_is_idempotent_never_plans_and_does_not_save_incomplete_training(self):
        fixture = CameraWorld()
        obs = fixture.capture()
        first = self.decide(obs)
        fixture.step(first)
        raw = encoded(fixture.capture())
        with patch.object(self.api.HypothesisAgent, "plan", side_effect=AssertionError("finish plans")):
            response = self.service.finish(raw)
            self.assertEqual(response, self.service.finish(raw))
        result = json.loads(response)
        self.assertEqual(result["status"], "closed")
        self.assertNotIn("action", result)
        self.assertFalse(self.model.exists())
        self.reject(encoded(obs), 409)
        self.reject(raw + b" ", 409, finish=True)

    def test_pause_same_observation_does_not_execute_pending_proposal(self):
        obs = CameraWorld().capture()
        decision = self.decide(obs)
        agent = self.service.sessions[obs["session"]].agent
        checkpoint, used = agent.core.checkpoint(), agent.budget.used
        result = json.loads(self.service.finish(encoded(obs)))
        self.assertEqual(result["resolution"], "unresolved")
        self.assertTrue(result["pending"])
        self.assertFalse(result["confirmed"])
        self.assertEqual(agent.core.checkpoint(), checkpoint)
        self.assertEqual(agent.budget.used, used)
        self.assertFalse(self.events(decision, "navigation_calibration"))
        self.assertFalse(self.model.exists())

    def test_cancelled_finish_never_teaches_the_command(self):
        env = CameraWorld()
        for _ in range(8):
            obs = env.capture()
            decision = self.decide(obs)
            if decision["action"] == "move":
                break
            env.step(decision)
        self.assertEqual(decision["action"], "move")
        terminal = dict(obs, seq=obs["seq"] + 1, outcome=receipt(
            decision, (obs["x"], obs["z"]), (obs["x"], obs["z"]), "cancelled", 1700))
        result = json.loads(self.service.finish(encoded(terminal)))
        self.assertTrue(result["matched"])
        self.assertFalse(result["confirmed"])
        self.assertEqual(result["support"], decision["support"])
        self.assertFalse(self.model.exists())

    def test_finish_cannot_credit_teleport_after_wait(self):
        self.train()
        obs = observation(profile="learn", session="idle")
        for _ in range(6):
            decision = self.decide(obs)
            if decision["action"] == "wait":
                break
            self.assertEqual(decision["action"], "scan")
            obs = dict(obs, seq=obs["seq"] + 1, yaw=decision["yaw"],
                       outcome=receipt(decision, (3, 3), (3, 3)))
        self.assertEqual(decision["action"], "wait")
        fake = dict(obs, seq=obs["seq"] + 1, x=6, z=6, completed=True)
        self.reject(encoded(fake), 409, finish=True)

    def test_320_decisions_limit_retains_exact_retry_and_terminal_receipt(self):
        self.train()
        obs = observation(profile="trained")
        for sequence in range(320):
            obs["seq"] = sequence
            decision = self.decide(obs)
            raw = encoded(obs)
            if decision["action"] == "scan":
                obs.update(yaw=decision["yaw"], outcome=receipt(decision, (3, 3), (3, 3)))
        cached = self.service.process(raw)
        journal = Path(decision["journal"]).read_bytes()
        self.reject(encoded(dict(obs, seq=320)), 429)
        self.assertEqual(self.service.process(raw), cached)
        self.assertEqual(Path(decision["journal"]).read_bytes(), journal)
        result = json.loads(self.service.finish(encoded(dict(obs, seq=320))))
        self.assertEqual(result["status"], "closed")

    def test_dynamic_obstacle_between_decision_and_motor_gets_a_detour(self):
        fixture, _ = self.train()
        env = CameraWorld(fixture.effects, profile="trained", session="dynamic", world="development-fixture")
        blocked_edge = None
        for _ in range(120):
            obs = env.capture()
            decision = self.decide(obs)
            if decision["action"] == "done":
                self.assertTrue(env.obs["completed"])
                break
            self.assertIn(decision["action"], ("move", "scan"), decision)
            if decision["action"] == "move":
                origin = (obs["x"], obs["z"])
                edge = origin, decision["command"]
                if blocked_edge is None:
                    dx, dz = env.effects[decision["command"]]
                    env.walls.add((origin[0] + dx, origin[1] + dz))
                    blocked_edge = edge
                else:
                    self.assertNotEqual(edge, blocked_edge)
            env.step(decision)
        else:
            self.fail("did not reach goal after dynamic obstacle")
        self.assertIsNotNone(blocked_edge)

    def test_offset_ray_origin_and_forged_checkpoint(self):
        decision = self.decide(observation(rays=[dict(sx=6045, sz=5980, ex=8000, ez=5980, hit=False)]))
        self.assertIn(dict(x=4, z=3, state="free"), decision["cells"])
        self.train()
        document = json.loads(self.model.read_text())
        document["checkpoint"]["payload"]["model"]["support"][0] += 100
        self.model.write_bytes(encoded(document))
        self.reject(encoded(observation(profile="trained", session="corrupted")), 409)

    def test_completed_flag_without_goal_pose_cannot_save_or_claim_done(self):
        self.reject(encoded(observation(profile="learn", completed=True)), 400)
        self.assertFalse(self.model.exists())

    def test_remembered_map_survives_restart_and_isolated_by_world(self):
        self.train()
        service = self.api.NavigationService(self.root / "remember", self.model)
        self.addCleanup(service.close)
        obs = observation(profile="remembered", session="save-map", world="world-one", rays=[
            dict(sx=6000, sz=6000, ex=10000, ez=6000, hit=True)])
        first = self.decide(obs, service)
        service.finish(encoded(obs))  # Memory is valid even when this episode is interrupted.
        service.close()
        restored = self.api.NavigationService(self.root / "remember", self.model)
        self.addCleanup(restored.close)
        same = self.decide(observation(profile="remembered", session="same", world="world-one"), restored)
        other = self.decide(observation(profile="remembered", session="other", world="world-two"), restored)
        self.assertIn(dict(x=5, z=3, state="occupied"), same["cells"])
        self.assertEqual(other["cells"], [dict(x=3, z=3, state="free")])
        self.assertEqual(first["modelHash"], same["modelHash"])

    def test_trained_visit_persists_map_for_remembered_after_restart(self):
        self.train()
        service = self.api.NavigationService(self.root / "revisit", self.model)
        self.addCleanup(service.close)
        obs = observation(profile="trained", session="visit", world="visited-world", rays=[
            dict(sx=6000, sz=6000, ex=10000, ez=6000, hit=True)])
        first = self.decide(obs, service)
        service.finish(encoded(obs))
        service.close()
        restored = self.api.NavigationService(self.root / "revisit", self.model)
        self.addCleanup(restored.close)
        same = self.decide(observation(profile="remembered", session="return", world="visited-world"), restored)
        self.assertIn(dict(x=5, z=3, state="occupied"), same["cells"])
        self.assertEqual(first["modelHash"], same["modelHash"])

    def test_closed_sessions_do_not_exhaust_active_limit_and_old_ids_do_not_replay(self):
        for index in range(100):
            obs = observation(session=f"closed-{index}")
            self.decide(obs)
            self.service.finish(encoded(obs))
        self.assertLessEqual(len(self.service.sessions), 64)
        recent = encoded(observation(session="closed-99"))
        self.assertEqual(self.service.finish(recent), self.service.finish(recent))
        self.reject(encoded(observation(session="closed-0")), 409)
        for index in range(16):
            self.decide(observation(session=f"active-{index}"))
        self.reject(encoded(observation(session="active-excess")), 503)

    def test_budget_exhaustion_waits_and_finish_still_records(self):
        obs = observation()
        first = self.decide(obs)
        session = self.service.sessions[obs["session"]]
        session.agent.budget.consume("test_remaining", session.agent.budget.remaining)
        obs.update(seq=1, yaw=first["yaw"], outcome=receipt(first, (3, 3), (3, 3)))
        second = self.decide(obs)
        self.assertEqual(second["action"], "wait")
        self.assertEqual(second["budgetRemaining"], 0)
        self.assertEqual(second["budgetUsed"], 2_000_000)
        result = json.loads(self.service.finish(encoded(obs)))
        self.assertEqual(result["status"], "closed")

    def test_fail_closed_after_journal_error_and_service_close(self):
        obs = observation()
        first = self.decide(obs)
        session = self.service.sessions[obs["session"]]
        obs.update(seq=1, yaw=first["yaw"], outcome=receipt(first, (3, 3), (3, 3)))
        with patch.object(session.recorder, "write", side_effect=OSError("disk full")):
            with self.assertRaises(OSError):
                self.service.process(encoded(obs))
        self.reject(encoded(obs), 503)
        self.service.close()
        self.reject(encoded(observation(session="new")), 503)

    def test_new_http_endpoints_preserve_old_server_constructor(self):
        server_api = importlib.import_module("server")
        from station_agent import BridgeService
        legacy = BridgeService(artifact_root=self.root / "legacy")
        self.addCleanup(legacy.close)
        httpd = server_api.make_server(port=0, service=legacy, navigation_service=self.service)
        thread = threading.Thread(target=httpd.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(httpd.server_close)
        self.addCleanup(thread.join, 3)
        self.addCleanup(httpd.shutdown)

        def post(path, raw, extra=None):
            client = http.client.HTTPConnection("127.0.0.1", httpd.server_port, timeout=3)
            self.addCleanup(client.close)
            client.request("POST", path, raw, headers={"Content-Type": "application/json", **(extra or {})})
            response = client.getresponse()
            return response.status, response.read()

        raw = encoded(observation())
        status, response = post("/navigate", raw)
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(response)["schema"], "echo.navigation.decision.v1")
        self.assertEqual(post("/navigate", raw), (200, response))
        self.assertEqual(post("/navigate", raw, {"Origin": "https://example.invalid"})[0], 403)
        self.assertEqual(post("/navigate/finish", raw)[0], 200)
        self.assertEqual(post("/navigate/finish", raw)[0], 200)

    def test_cli_navigation_root_is_used_by_fresh_server(self):
        root = self.root / "physical isolated root"
        server_script = Path(__file__).resolve().parents[1] / "server.py"
        process = subprocess.Popen([sys.executable, "-B", str(server_script), "--port", "0",
                                    "--navigation-root", str(root)], stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, text=True)
        try:
            ready, _, _ = select.select([process.stdout], [], [], 5)
            self.assertTrue(ready, "server did not announce readiness")
            line = process.stdout.readline()
            self.assertIn("http://127.0.0.1:", line, line)
            port = int(line.split("http://127.0.0.1:", 1)[1].split(";", 1)[0])
            client = http.client.HTTPConnection("127.0.0.1", port, timeout=3)
            try:
                client.request("POST", "/navigate", encoded(observation()),
                               {"Content-Type": "application/json"})
                response = client.getresponse()
                self.assertEqual(response.status, 200)
                decision = json.loads(response.read())
                self.assertTrue(Path(decision["journal"]).is_relative_to(root))
                self.assertTrue(Path(decision["journal"]).is_file())
                self.assertFalse((root / "model.json").exists())
            finally:
                client.close()
        finally:
            process.terminate()
            process.wait(timeout=5)
            process.stdout.close()


if __name__ == "__main__":
    unittest.main()
