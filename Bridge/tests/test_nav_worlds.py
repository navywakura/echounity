"""Evaluator tests use synthetic/acquisition/development geometry, never heldout."""
import importlib
import copy
import json
import math
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))


class WorldTests(unittest.TestCase):
    def test_generator_is_available(self):
        self.assertIsNotNone(importlib.util.find_spec("Tools.make_navigation_worlds"))

    def test_development_topologies_and_acquisition_clearance(self):
        from Tools.make_navigation_worlds import make_case, validate_case
        layouts = set()
        for family in ("rooms", "corridors", "barriers", "scatter"):
            for split in ("acquisition", "development"):
                for seed in (101, 202, 303):
                    case = make_case(family, seed, split=split)
                    validate_case(case)
                    self.assertEqual(set(case), set("id split family width height start goal walls mapping".split()))
                    self.assertEqual(case["mapping"], [dict(x=-1,z=0), dict(x=0,z=1), dict(x=1,z=0), dict(x=0,z=-1)])
                    self.assertRegex(case["id"], r"^c-[0-9a-f]{24}$")
                    walls = {(p["x"], p["z"]) for p in case["walls"]}
                    w, h = case["width"], case["height"]
                    self.assertLessEqual(max(w, h), 16)
                    self.assertTrue(all((x, z) in walls for x in range(w) for z in (0, h-1)))
                    self.assertTrue(all((x, z) in walls for z in range(h) for x in (0, w-1)))
                    if split == "acquisition":
                        self.assertGreaterEqual(min(w, h), 13)
                        self.assertEqual(case["start"], {"x":7,"z":7})
                        self.assertFalse(walls & {(x,z) for x in range(4,11) for z in range(4,11)})
                    topology = (w,h,tuple(sorted(walls)))
                    self.assertNotIn(topology, layouts)
                    layouts.add(topology)

    def test_disconnected_goal_is_rejected(self):
        from Tools.make_navigation_worlds import make_case, validate_case
        case = make_case("scatter", 55)
        goal = case["goal"]
        for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
            point = dict(x=goal["x"]+dx,z=goal["z"]+dz)
            if point not in case["walls"]:
                case["walls"].append(point)
        with self.assertRaises(ValueError):
            validate_case(case)


def fixture(walls=(), start=(4,4), goal=(6,6), size=9):
    boundary = {(x,z) for x in range(size) for z in range(size) if x in (0,size-1) or z in (0,size-1)}
    return {"id":"c-0123456789abcdef01234567", "split":"development", "family":"scatter",
            "width":size,"height":size,"start":dict(zip(("x","z"),start)),"goal":dict(zip(("x","z"),goal)),
            "walls":[dict(x=x,z=z) for x,z in sorted(boundary | set(walls))],
            "mapping":[dict(x=-1,z=0),dict(x=0,z=1),dict(x=1,z=0),dict(x=0,z=-1)]}


class SensorTests(unittest.TestCase):
    def test_emulator_is_available(self):
        self.assertIsNotNone(importlib.util.find_spec("Tools.nav_benchmark"))

    def test_box_faces_parallel_miss_and_inside(self):
        from Tools.nav_benchmark import ray_box_distance
        self.assertAlmostEqual(ray_box_distance((0,0),(0,1),(0,2000)), 1040)
        self.assertIsNone(ray_box_distance((0,0),(0,1),(2000,0)))
        self.assertIsNone(ray_box_distance((0,0),(0,1),(0,-2000)))
        self.assertIsNone(ray_box_distance((0,0),(0,1),(0,12000)))
        self.assertEqual(ray_box_distance((0,0),(1,0),(0,0)), 0)
        self.assertAlmostEqual(ray_box_distance((0,0),(2**-.5,2**-.5),(2000,2000)), 1040*2**.5)

    def test_sensor_occlusion_yaw_and_raw_public_observation(self):
        from Tools.nav_benchmark import NavigationEnvironment, assert_public_observation
        env = NavigationEnvironment(fixture(walls=((4,5),(4,6),(5,4),(4,3))))
        obs = env.observation("unit",0,"cold")
        assert_public_observation(obs)
        self.assertEqual(len(obs["rays"]), 61)
        self.assertEqual(obs["rays"][30], {"sx":8000,"sz":8000,"ex":8000,"ez":9040,"hit":True})
        env.apply({"session":"unit","seq":0,"action":"scan","command":-1,"yaw":90})
        obs = env.observation("unit",1,"cold")
        self.assertEqual(obs["rays"][30], {"sx":8000,"sz":8000,"ex":9040,"ez":8000,"hit":True})
        self.assertEqual(obs["outcome"]["status"], "scanned")
        self.assertNotIn("walls",obs)
        self.assertNotIn("mapping",obs)
        self.assertNotIn("family",obs)
        for key in ("walls", "mapping", "seed", "truth", "cells", "family"):
            with self.assertRaises(ValueError):
                assert_public_observation(dict(obs, **{key:[]}))
        bad = json.loads(json.dumps(obs))
        bad["rays"][0]["objectId"] = "secret"
        with self.assertRaises(ValueError):
            assert_public_observation(bad)

    def test_projected_pixels_not_equal_angle_and_ten_metre_range(self):
        from Tools.nav_benchmark import NavigationEnvironment
        env = NavigationEnvironment(fixture(size=16,start=(7,7),goal=(12,12)))
        rays = env.observation("unit",0,"cold")["rays"]
        self.assertTrue(all(not r["hit"] for r in rays))
        for i in (0,15,30,45,60):
            ray = rays[i]
            dx, dz = ray["ex"]-ray["sx"], ray["ez"]-ray["sz"]
            self.assertAlmostEqual(math.hypot(dx,dz), 10000, delta=1)
            self.assertAlmostEqual(dx/dz, (i/30-1)*(383/384)*math.tan(math.radians(50)), delta=.0002)
        self.assertAlmostEqual(math.degrees(math.atan2(rays[45]["ex"]-14000,rays[45]["ez"]-14000)), 30.724, delta=.01)

    def test_blocked_motion_has_actual_pose_and_goal_is_position_judged(self):
        from Tools.nav_benchmark import NavigationEnvironment
        env = NavigationEnvironment(fixture(walls=((5,4),),goal=(4,5)))
        env.apply({"session":"unit","seq":0,"action":"move","command":2,"yaw":0,
                   "reason":"goal reached","plan":[{"x":4,"z":5}]})
        obs = env.observation("unit",1,"cold")
        self.assertEqual((obs["x"],obs["z"]), (4,4))
        self.assertFalse(obs["completed"])
        self.assertEqual(obs["outcome"], dict(present=True,session="unit",seq=0,action="move",command=2,
                                             status="blocked",fromX=4,fromZ=4,x=4,z=4,distanceMm=2000))
        env.apply({"session":"unit","seq":1,"action":"move","command":1,"yaw":0})
        obs = env.observation("unit",2,"cold")
        self.assertEqual((obs["x"],obs["z"]), (4,5))
        self.assertTrue(obs["completed"])
        self.assertEqual(obs["outcome"]["distanceMm"], 0)
        self.assertEqual(obs["outcome"]["status"], "arrived")


class ScriptedService:
    """Controlled actor proposals exercise the real independent evaluator."""
    def __init__(self, commands):
        self.commands = iter(commands)

    def process(self, raw):
        obs = json.loads(raw)
        command = next(self.commands, -1)
        action = "move" if command >= 0 else ("scan" if command == -2 else "done")
        return json.dumps(dict(schema="echo.navigation.decision.v1",session=obs["session"],seq=obs["seq"],
            action=action,command=command if command >= 0 else -1,yaw=90,reason="Invented success!",
            prediction="",evidence=[],hypotheses=[],cells=[],plan=[],support=[0]*4,learnedActions=0,
            budgetUsed=obs["seq"]+1,budgetRemaining=2000000-obs["seq"]-1,modelHash="empty",journal="")).encode()

    def finish(self, raw):
        obs = json.loads(raw)
        return json.dumps(dict(schema="echo.navigation.finish.v1",session=obs["session"],seq=obs["seq"],status="closed")).encode()


class AuditTests(unittest.TestCase):
    def test_evaluator_interface(self):
        from Tools import nav_benchmark
        self.assertTrue(callable(getattr(nav_benchmark,"run_episode",None)))

    def test_position_scoring_and_complete_replay_reject_forged_transcripts(self):
        from Tools.nav_benchmark import run_episode, audit_transcript
        case = fixture(walls=((5,4),),goal=(4,5))
        with tempfile.TemporaryDirectory() as temp:
            result = run_episode(ScriptedService([2,1]),case,"cold",Path(temp)/"episode")
            self.assertTrue(result["success"])
            self.assertEqual((result["moves"],result["actualSteps"],result["failures"],result["actions"]),(2,1,1,2))
            self.assertEqual(result["goal"], {"x":4,"z":5})
            self.assertEqual(result["finalPosition"], {"x":4,"z":5})
            self.assertTrue(audit_transcript(result["transcript"],case)["valid"])
            rows = [json.loads(line) for line in Path(result["transcript"]).read_text().splitlines()]
            move = next(row for row in rows if row["kind"]=="navigation_evaluation_step")
            self.assertEqual(move["observation"]["x"],4)
            self.assertEqual(move["proposal"]["command"],2)
            self.assertEqual(move["outcome"]["status"],"blocked")
            # Rehashing is deliberately omitted: the inherited journal verifier must reject editing.
            move["after"]["x"] = 5
            forged = Path(temp)/"forged.jsonl"
            forged.write_text("\n".join(json.dumps(row) for row in rows)+"\n")
            with self.assertRaises(ValueError):
                audit_transcript(forged,case)
            from Tools.nav_benchmark import core_api
            Recorder,_ = core_api()
            resealed = Path(temp)/"resealed.jsonl"
            with Recorder(resealed) as recorder:
                for row in rows:
                    recorder.write(**{k:v for k,v in row.items() if k not in ("schema","index","time","prev","hash","agent")})
            with self.assertRaises(ValueError):
                audit_transcript(resealed,case)

    def test_done_claim_does_not_make_failure_a_success(self):
        from Tools.nav_benchmark import run_episode
        with tempfile.TemporaryDirectory() as temp:
            result = run_episode(ScriptedService([]),fixture(),"cold",Path(temp))
            self.assertFalse(result["success"])
            self.assertEqual(result["actualSteps"],0)
            self.assertEqual(result["actions"],0)
            self.assertEqual(result["decisions"],1)
            terminal = json.loads(Path(result["transcript"]).read_text().splitlines()[-1])
            self.assertEqual(terminal["observation"]["seq"],0)

    def test_decisions_are_capped_without_stopping_terminal_receipt(self):
        from Tools.nav_benchmark import run_episode
        with tempfile.TemporaryDirectory() as temp:
            result = run_episode(ScriptedService([-2]*400),fixture(),"cold",Path(temp))
            self.assertEqual(result["decisions"],320)
            self.assertEqual(result["scans"],320)
            self.assertFalse(result["success"])
            self.assertTrue(result["audit"]["valid"])
            terminal = json.loads(Path(result["transcript"]).read_text().splitlines()[-1])
            self.assertEqual(terminal["observation"]["seq"],320)

    def test_frozen_profile_cannot_change_model_in_memory(self):
        from Tools.nav_benchmark import run_episode
        class ChangingService(ScriptedService):
            def process(self, raw):
                value = json.loads(super().process(raw))
                value.update(modelHash="frozen" if value["seq"]==0 else "changed",support=[2]*4,learnedActions=4)
                return json.dumps(value).encode()
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                run_episode(ChangingService([1,1]),fixture(goal=(4,6)),"trained",Path(temp),expected_model_hash="frozen")

    def test_resealed_terminal_cannot_invent_work_or_a_different_finish_sequence(self):
        from Tools.nav_benchmark import run_episode,audit_transcript,core_api
        Recorder,_ = core_api()
        case = fixture(goal=(4,5))
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            episode = run_episode(ScriptedService([1]),case,"cold",root/"episode")
            original = [json.loads(line) for line in Path(episode["transcript"]).read_text().splitlines()]
            for field in ("work","seq"):
                rows = copy.deepcopy(original)
                if field == "work":
                    rows[-1]["work"] += 1000
                else:
                    rows[-1]["acknowledgement"]["seq"] += 1
                    rows[-1]["responseUtf8"] = json.dumps(rows[-1]["acknowledgement"])
                path = root/(field+".jsonl")
                with Recorder(path) as recorder:
                    for row in rows:
                        recorder.write(**{k:v for k,v in row.items() if k not in ("schema","index","time","prev","hash","agent")})
                with self.subTest(field=field),self.assertRaises(ValueError):
                    audit_transcript(path,case)

    def test_transfer_gate_never_labels_development_as_valid_transfer(self):
        from Tools.nav_benchmark import classify_transfer
        rows = [dict(case=str(i),profile=p,success=True,actions=10 if p!="cold" else 14,actualSteps=8 if p!="cold" else 12,
                     work=100,decisions=12,audit={"valid":True},restorationVerified=p!="cold")
                for i in range(16) for p in ("trained","cold","no_memory")]
        acquisition = {"reproducible":True}
        self.assertFalse(classify_transfer(rows,"development",acquisition)["validTransfer"])
        self.assertTrue(classify_transfer(rows,"confirmation",acquisition)["validTransfer"])
        rows[0]["success"] = False
        self.assertFalse(classify_transfer(rows,"confirmation",acquisition)["validTransfer"])
        rows[0]["success"] = True
        rows[0]["audit"]["valid"] = False
        self.assertFalse(classify_transfer(rows,"confirmation",acquisition)["validTransfer"])


class CampaignTests(unittest.TestCase):
    def test_launcher_interface(self):
        from Tools import nav_benchmark
        self.assertTrue(callable(getattr(nav_benchmark,"parse_args",None)))

    def test_confirmation_requires_explicit_freeze_and_worker_cap(self):
        from Tools.nav_benchmark import parse_args
        with patch("sys.stderr"):
            with self.assertRaises(SystemExit):
                parse_args(["--phase","confirmation"])
            with self.assertRaises(SystemExit):
                parse_args(["--phase","development","--workers","3"])
        args = parse_args(["--phase","development"])
        self.assertFalse(args.worker)
        self.assertEqual(args.workers,2)
        self.assertIsNone(args.freeze)

    def test_snapshot_refuses_changed_source_and_checkpoint(self):
        from Tools.nav_benchmark import snapshot_files, verify_snapshot
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source, checkpoint = root/"source.py", root/"motor.json"
            source.write_text("x=1\n")
            checkpoint.write_text('{"checkpoint":1}\n')
            manifest = snapshot_files({"source.py":source,"motor.json":checkpoint},root/"snapshot")
            verify_snapshot(manifest,root/"snapshot",check_current=True)
            source.write_text("x=2\n")
            with self.assertRaises(ValueError):
                verify_snapshot(manifest,root/"snapshot",check_current=True)
            source.write_text("x=1\n")
            (root/"snapshot/motor.json").write_text('{"checkpoint":2}\n')
            with self.assertRaises(ValueError):
                verify_snapshot(manifest,root/"snapshot",check_current=True)

    def test_source_freeze_includes_lazily_loaded_core_dependencies(self):
        from Tools.nav_benchmark import core_api, source_files, source_hash
        core_api()
        before = source_hash(source_files())
        from echo5.budget import Budget
        from echo5.interaction import HypothesisAgent
        obs = dict(schema="echo5.interaction.observation.v1",width=16,height=16,walls=[],
                   position=[7,7],goal=[12,12],actions=[0,1,2,3],tick=0)
        agent = HypothesisAgent()
        agent.observe(obs,0,dict(obs,position=[7,6],tick=1),budget=Budget(2000000))
        self.assertEqual(before,source_hash(source_files()))
        # Bind the implementation actually imported by the core. The official
        # standalone checkout places historical navi10_qn in echoai/core/qn.py.
        from echo5.legacy import module
        qn_module = sys.modules[module("nexus0.loop").simhash16.__module__]
        self.assertIn(Path(qn_module.__file__).resolve(),
                      {path.resolve() for path in source_files().values()})

    def test_acquisition_needs_two_physical_samples_per_identified_command(self):
        from Tools.nav_benchmark import run_episode, acquired_model_receipt, core_api
        core_api()
        from echo5.interaction import HypothesisAgent
        from echo5.budget import Budget
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            episode = run_episode(ScriptedService([0,0,1,1,2,2,3,3]),
                fixture(size=16,start=(7,7),goal=(12,12)),"learn",root/"episode")
            outcomes = [row["outcome"] for line in Path(episode["transcript"]).read_text().splitlines()
                        if (row:=json.loads(line)).get("outcome")]
            for per_command, expected in ((1,False),(2,True)):
                core, counts = HypothesisAgent(), {}
                for outcome in outcomes:
                    command = outcome["command"]
                    if counts.get(command,0) >= per_command:
                        continue
                    counts[command] = counts.get(command,0)+1
                    before = dict(schema="echo5.interaction.observation.v1",width=16,height=16,walls=[],
                        position=[outcome["fromZ"],outcome["fromX"]],goal=[0,0],actions=[0,1,2,3],tick=0)
                    after = dict(before,position=[outcome["z"],outcome["x"]],tick=1)
                    core.observe(before,command,after,budget=Budget(2000000))
                model = root/"model.json"
                model.write_text(json.dumps({"checkpoint":core.checkpoint(),"acquisition":[{"test":"physical transcript"}]}))
                receipt = acquired_model_receipt(model,[episode])
                self.assertEqual(receipt["reproducible"],expected)

    def test_raw_actor_input_and_import_boundary(self):
        # Fresh process: deny both dataset reads and evaluator imports before actor import.
        if not (ROOT/"Bridge/navigation.py").exists():
            self.skipTest("Locke's NavigationService is not ready yet")
        import subprocess
        script = '''
import json, pathlib, sys, tempfile
sys.dont_write_bytecode=True
sys.path.insert(0, str(pathlib.Path.cwd()/"Bridge"))
def guard(event,args):
    if event=="open" and isinstance(args[0],(str,bytes)):
        path=str(args[0])
        if "worlds.json" in path or "seed-manifest" in path or "make_navigation_worlds" in path or "nav_benchmark" in path:
            raise AssertionError("actor tried to read private evaluator data: "+path)
    if event=="import" and any(word in args[0] for word in ("nav_benchmark","make_navigation_worlds")):
        raise AssertionError("actor imported evaluator")
sys.addaudithook(guard)
from navigation import NavigationService
with tempfile.TemporaryDirectory() as temp:
    service=NavigationService(pathlib.Path(temp)/"agent",model_path=pathlib.Path(temp)/"model.json")
    try:
        observation=json.loads(sys.stdin.read())
        decision=json.loads(service.process(json.dumps(observation).encode()))
        assert decision["schema"]=="echo.navigation.decision.v1"
        service.finish(json.dumps(observation).encode())
    finally:
        service.close()
'''
        from Tools.nav_benchmark import NavigationEnvironment
        raw = json.dumps(NavigationEnvironment(fixture()).observation("boundary",0,"cold"))
        result = subprocess.run([sys.executable,"-B","-c",script],input=raw,text=True,capture_output=True,cwd=ROOT,timeout=30)
        self.assertEqual(result.returncode,0,result.stderr)


if __name__ == "__main__":
    unittest.main()
