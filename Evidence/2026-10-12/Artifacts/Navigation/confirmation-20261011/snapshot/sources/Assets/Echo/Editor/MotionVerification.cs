// Behavioral contracts were written before the motor/visual implementation.
// Run in the parent's serial Unity editor; no NUnit or external packages required.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EchoStation
{
    public static class MotionVerification
    {
        [Serializable]
        public sealed class CaseResult
        {
            public string name;
            public bool passed;
            public string detail;
            public double milliseconds;
        }

        [Serializable]
        public sealed class Results
        {
            public string unityVersion;
            public string utc;
            public bool passed;
            public int failures;
            public List<CaseResult> tests = new List<CaseResult>();
        }

        [MenuItem("ECHO/Verify Motion")]
        private static void RunMenu() { Run(); }

        /// <summary>Writes Logs/motion-tests.json even when a behavioral assertion fails.</summary>
        public static Results Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run motion verification in edit mode.");
            var results = new Results { unityVersion = Application.unityVersion,
                utc = DateTime.UtcNow.ToString("O") };
            Check(results, "arrival_and_no_drift", Arrival);
            Check(results, "acceleration_and_explicit_stop", AccelerationAndStop);
            Check(results, "frontal_wall_unreachable", () => Wall(1f));
            Check(results, "reverse_wall_unreachable", () => Wall(-1f));
            Check(results, "corner_slides_to_reachable_target", Corner);
            Check(results, "large_dt_thin_wall_no_tunnelling", HighDelta);
            Check(results, "initial_overlap_never_depenetrates", InitialOverlap);
            Check(results, "own_hierarchy_and_nonworld_layers_ignored", LayerFiltering);
            Check(results, "invalid_inputs_are_inert", InvalidInputs);
            Check(results, "retarget_and_blocked_state_reset", Retarget);
            Check(results, "prediction_toggle_and_curvature", Prediction);
            Check(results, "visual_build_ik_planting_and_cleanup", Visual);
            results.passed = results.failures == 0;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/motion-tests.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(results, true));
            if (!results.passed)
                throw new InvalidOperationException("Motion verification: " + results.failures +
                    " failure(s). See " + path + "\n" + FailureDetails(results));
            UnityEngine.Debug.Log("Motion verification: " + results.tests.Count + " passed. " + path);
            return results;
        }

        private static void Check(Results results, string name, Action test)
        {
            var item = new CaseResult { name = name };
            var watch = Stopwatch.StartNew();
            try { test(); item.passed = true; item.detail = "Behavior verified."; }
            catch (Exception ex) { item.detail = ex.ToString(); results.failures++; }
            finally { watch.Stop(); item.milliseconds = watch.Elapsed.TotalMilliseconds; results.tests.Add(item); }
        }

        private static string FailureDetails(Results results)
        {
            var lines = new List<string>();
            foreach (CaseResult item in results.tests)
                if (!item.passed) lines.Add(item.name + ": " + item.detail);
            return string.Join("\n", lines);
        }

        // A no-op Tick must fail arrival; removing collision checks must fail the walls.
        private static void Arrival()
        {
            using (var f = new Fixture())
            {
                f.motor.SetTarget(new Vector3(0f, 0f, 2f));
                float previousSpeed = 0f;
                for (int i = 0; i < 320; i++)
                {
                    f.Step(1);
                    Require(previousSpeed - f.motor.Speed <= .135f,
                        "Unobstructed arrival exceeded the braking cap.");
                    previousSpeed = f.motor.Speed;
                }
                Require(f.motor.Arrived && !f.motor.HasTarget && !f.motor.Blocked, "Target was not reached.");
                Require(Vector3.Distance(f.robot.position, new Vector3(0, 0, 2)) < .065f,
                    "Arrival reported away from the target.");
                Vector3 settled = f.robot.position;
                f.Step(160);
                Require(Vector3.Distance(settled, f.robot.position) < .0001f && f.motor.Speed < .0001f,
                    "Arrived robot drifted.");
                Require(Mathf.Abs(settled.y) < .005f, "XZ motion changed robot elevation.");
            }
        }

        private static void AccelerationAndStop()
        {
            using (var f = new Fixture())
            {
                f.motor.SetTarget(Vector3.forward * 8f);
                float previous = 0f;
                for (int i = 0; i < 60; i++)
                {
                    f.Step(1);
                    Require(f.motor.Speed <= 1.66f, "Speed cap exceeded.");
                    Require(f.motor.Speed - previous <= .101f, "Acceleration exceeded 5 m/s².");
                    previous = f.motor.Speed;
                }
                Require(f.robot.position.z > .8f, "Acceleration test cannot pass with an inert motor.");
                f.motor.Stop();
                Vector3 stopped = f.robot.position;
                f.Step(100);
                Require((f.robot.position - stopped).sqrMagnitude < .000001f,
                    "Explicit Stop continued moving.");
                Require(!f.motor.HasTarget && !f.motor.Arrived && !f.motor.Blocked, "Stop left stale state.");
            }
        }

        private static void Wall(float sign)
        {
            using (var f = new Fixture())
            {
                f.Box("Wall", new Vector3(0, 1.5f, sign), new Vector3(50, 3, .2f), 8);
                f.motor.SetTarget(Vector3.forward * (sign * 4));
                float furthest = 0;
                for (int i = 0; i < 650; i++)
                {
                    f.Step(1);
                    furthest = Mathf.Max(furthest, f.robot.position.z * sign);
                    Require(f.robot.position.z * sign <= .65f, "Capsule crossed the wall face.");
                }
                Require(furthest > .04f, "Robot never attempted the route.");
                Require(f.motor.Blocked && !f.motor.HasTarget && !f.motor.Arrived,
                    "Unreachable route did not terminate as blocked.");
                Require(f.motor.Speed < .0001f, "Blocked robot retained speed.");
            }
        }

        private static void Corner()
        {
            using (var f = new Fixture())
            {
                // A finite cabinet across the diagonal forces a local detour around its right edge.
                f.Box("Cabinet", new Vector3(-.6f, 1, 1.15f), new Vector3(2.4f, 2, .3f), 8);
                f.motor.SetTarget(new Vector3(1.5f, 0, 2.5f));
                for (int i = 0; i < 750 && f.motor.HasTarget; i++)
                {
                    f.Step(1);
                    Vector3 p = f.robot.position;
                    // This interior slab is strictly inside the inflated rectangular obstacle.
                    Require(!(p.x > -1.8f && p.x < .6f && p.z > .75f && p.z < 1.55f),
                        "Robot penetrated the cabinet while sliding.");
                }
                Require(f.motor.Arrived, "Local steering did not reach the accessible side of the cabinet.");
                Require(Vector3.Distance(f.robot.position, new Vector3(1.5f, 0, 2.5f)) < .065f,
                    "Corner test falsely reported arrival.");
            }
        }

        private static void HighDelta()
        {
            using (var f = new Fixture())
            {
                f.Box("Thin wall", new Vector3(0, 1, .65f), new Vector3(40, 2, .01f), 8);
                f.motor.SetTarget(Vector3.forward * 10);
                for (int i = 0; i < 150; i++)
                {
                    Vector3 before = f.robot.position;
                    f.Step(1, 20f);
                    Require(Vector3.Distance(before, f.robot.position) <= .091f,
                        "A stalled frame caused an unbounded displacement.");
                    Require(f.robot.position.z < .395f, "Large dt tunneled through a 1 cm wall.");
                }
            }
        }

        private static void InitialOverlap()
        {
            using (var f = new Fixture())
            {
                f.Box("Spawn overlap", new Vector3(0, .7f, .04f), new Vector3(.15f, 1.4f, .15f), 8);
                f.motor.SetTarget(Vector3.forward * 3);
                Vector3 start = f.robot.position;
                f.Step(80);
                Require(Vector3.Distance(start, f.robot.position) < .0001f,
                    "Initial overlap recovery displaced the robot through geometry.");
                Require(f.motor.Blocked && !f.motor.HasTarget, "Initial overlap must fail closed.");
            }
        }

        private static void LayerFiltering()
        {
            using (var f = new Fixture())
            {
                f.Box("Decoration", new Vector3(0, 1, 1), new Vector3(8, 2, .2f), 0);
                GameObject self = new GameObject("Own collider");
                self.transform.SetParent(f.robot, false);
                self.layer = 8;
                BoxCollider own = self.AddComponent<BoxCollider>();
                own.center = new Vector3(0, .65f, .1f);
                own.size = new Vector3(.3f, .6f, .3f);
                f.Box("World trigger", new Vector3(0, 1, 1.5f), new Vector3(8, 2, .2f), 8).isTrigger = true;
                f.motor.SetTarget(Vector3.forward * 2);
                f.Step(400);
                Require(f.motor.Arrived, "Own hierarchy, a trigger, or nonworld decoration obstructed movement.");
            }
        }

        private static void InvalidInputs()
        {
            using (var f = new Fixture())
            {
                f.motor.SetTarget(Vector3.forward);
                f.motor.Tick(float.NaN);
                f.motor.Tick(float.PositiveInfinity);
                f.motor.Tick(-1f);
                f.motor.Tick(0f);
                Require(f.robot.position.sqrMagnitude < .000001f, "Invalid dt moved the motor.");
                f.motor.SetTarget(new Vector3(float.NaN, 0, 1));
                f.Step(10);
                Require(Finite(f.robot.position) && f.robot.position.sqrMagnitude < .000001f,
                    "Nonfinite target corrupted or moved the transform.");
                Require(!f.motor.HasTarget, "Nonfinite target remained active.");
            }
        }

        private static void Retarget()
        {
            using (var f = new Fixture())
            {
                f.Box("Target occupancy", new Vector3(0, 1, 2), new Vector3(1, 2, 1), 8);
                f.motor.SetTarget(Vector3.forward * 2);
                f.Step(400);
                Require(f.motor.Blocked && !f.motor.Arrived, "An occupied target reported success.");
                f.motor.SetTarget(new Vector3(-2, 0, 0));
                f.Step(400);
                Require(f.motor.Arrived && !f.motor.Blocked, "Retarget failed to clear previous blockage.");
            }
        }

        private static void Prediction()
        {
            using (var f = new Fixture())
            {
                f.motor.ShowPrediction = true;
                f.motor.SetTarget(Vector3.forward * 5);
                f.Step(40);
                f.motor.SetTarget(new Vector3(3, 0, 2));
                f.Step(1);
                LineRenderer[] lines = f.robot.GetComponentsInChildren<LineRenderer>(true);
                Require(lines.Length == 2, "Expected front and rear prediction chains.");
                bool curved = false;
                foreach (LineRenderer line in lines)
                {
                    Require(line.enabled && line.positionCount >= 3, "Prediction chain is missing samples.");
                    for (int i = 2; i < line.positionCount; i++)
                    {
                        Vector3 a = line.GetPosition(i - 1) - line.GetPosition(i - 2);
                        Vector3 b = line.GetPosition(i) - line.GetPosition(i - 1);
                        curved |= Vector3.Cross(a, b).sqrMagnitude > .00000001f;
                    }
                }
                Require(curved, "Turn prediction was a straight ray instead of a curved chain.");
                f.motor.ShowPrediction = false;
                foreach (LineRenderer line in lines)
                    Require(!line.enabled || !line.gameObject.activeInHierarchy, "V toggle left a prediction visible.");
                Material owned = lines[0].sharedMaterial;
                f.motor.ShowPrediction = true;
                Require(f.robot.GetComponentsInChildren<LineRenderer>(true).Length == 2 &&
                    lines[0].sharedMaterial == owned, "Prediction toggle leaked new geometry/materials.");
                Object.DestroyImmediate(f.robot.gameObject);
                Require(!owned, "Destroying edit-time predictions leaked their owned material.");
            }
        }

        private static void Visual()
        {
            using (var f = new Fixture())
            {
                RobotVisual visual = RobotVisual.Build(f.robot);
                Require(visual && RobotVisual.Build(f.robot) == visual, "Build must be safe to repeat.");
                Renderer[] pieces = visual.GetComponentsInChildren<Renderer>(true);
                Require(pieces.Length >= 25, "Robot lacks articulated visual pieces.");
                Require(visual.GetComponentsInChildren<Collider>(true).Length == 0, "Visuals created physical colliders.");
                var materials = new HashSet<Material>();
                foreach (Renderer piece in pieces) materials.Add(piece.sharedMaterial);
                Require(materials.Count <= 9, "Materials were allocated per visual piece.");
                Transform left = Find(visual.transform, "Left Foot");
                Transform right = Find(visual.transform, "Right Foot");
                Require(left && right, "Both articulated feet must be identifiable.");
                Vector3 leftIdle = left.position, rightIdle = right.position;
                for (int i = 0; i < 60; i++) visual.Tick(.02f);
                Require(Vector3.Distance(left.position, leftIdle) < .003f &&
                    Vector3.Distance(right.position, rightIdle) < .003f, "Stationary feet drifted.");
                f.motor.SetTarget(Vector3.forward * 3);
                int plantedSamples = 0, liftedSamples = 0;
                for (int i = 0; i < 280; i++)
                {
                    Vector3 lastLeft = left.position, lastRight = right.position;
                    f.Step(1);
                    visual.Tick(.02f);
                    if (f.motor.Speed > .1f)
                    {
                        if (Vector3.Distance(left.position, lastLeft) < .002f ||
                            Vector3.Distance(right.position, lastRight) < .002f) plantedSamples++;
                        if (left.position.y > .025f || right.position.y > .025f) liftedSamples++;
                    }
                    foreach (Transform bone in visual.GetComponentsInChildren<Transform>(true))
                        Require(Finite(bone.position) && Finite(bone.localScale) && Finite(bone.rotation),
                            "IK generated a nonfinite pose: " + bone.name);
                }
                Require(plantedSamples > 20 && liftedSamples > 10, "Gait did not plant and lift feet during actual travel.");
                foreach (Renderer piece in pieces)
                    Require(materials.Contains(piece.sharedMaterial), "Gait allocated or replaced a shared material.");
                Object.DestroyImmediate(visual.gameObject);
                foreach (Material material in materials)
                    Require(!material, "Destroying a robot leaked an owned procedural material.");
            }
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private static bool Finite(Quaternion q) { return Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w); }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly GameObject root;
            public readonly Transform robot;
            public readonly PredictiveMotor motor;

            public Fixture()
            {
                // Preview scenes are the edit-mode API; CreateScene(localPhysicsMode)
                // is play-mode-only. Never close or replace any of the user's scenes.
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    Require(scene.GetPhysicsScene().IsValid(), "Preview scene has no physics scene.");
                    Require(scene.GetPhysicsScene() != Physics.defaultPhysicsScene,
                        "Preview fixture requires physics isolation from open user scenes.");
                    root = new GameObject("Motion verification fixtures");
                    SceneManager.MoveGameObjectToScene(root, scene);
                    Box("Ground", new Vector3(0, -.1f, 0), new Vector3(100, .2f, 100), 9);
                    robot = new GameObject("Test robot").transform;
                    robot.SetParent(root.transform, false);
                    motor = robot.gameObject.AddComponent<PredictiveMotor>();
                    Physics.SyncTransforms();
                }
                catch
                {
                    if (root) Object.DestroyImmediate(root);
                    EditorSceneManager.ClosePreviewScene(scene);
                    throw;
                }
            }

            public BoxCollider Box(string name, Vector3 position, Vector3 size, int layer)
            {
                var item = new GameObject(name);
                item.transform.SetParent(root.transform, false);
                item.transform.position = position;
                item.layer = layer;
                BoxCollider collider = item.AddComponent<BoxCollider>();
                collider.size = size;
                Physics.SyncTransforms();
                return collider;
            }

            public void Step(int count, float dt = .02f)
            {
                for (int i = 0; i < count; i++)
                {
                    Physics.SyncTransforms();
                    motor.Tick(dt);
                    Physics.SyncTransforms();
                }
            }

            public void Dispose()
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
                Physics.SyncTransforms();
            }
        }
    }
}
#endif
