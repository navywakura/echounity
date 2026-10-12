// Written before RobotSensor. Run only in the coordinator's existing Unity editor.
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
    public static class SensorVerification
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

        [MenuItem("ECHO/Verify RGB-D Sensor")]
        private static void RunMenu() { Run(); }

        /// <summary>Uses isolated edit-mode physics; always reports assertion failures before throwing.</summary>
        public static Results Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run sensor verification in edit mode.");
            var results = new Results {
                unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O")
            };
            Check(results, "front_wall_and_occluded_endpoints", Occlusion);
            Check(results, "behind_camera_not_observed", Behind);
            Check(results, "ten_metre_range_from_eye", Range);
            Check(results, "odometry_origin_and_camera_frustum", OriginAndFrustum);
            Check(results, "absolute_rotated_fov_and_yaw_wrapping", RotatedFieldOfView);
            Check(results, "outside_fov_not_observed", OutsideFieldOfView);
            Check(results, "own_identity_layers_and_triggers", OwnAndLayers);
            Check(results, "physics_only_without_map_or_semantics", NoMapOrSemantics);
            Check(results, "preview_physics_scenes_are_isolated", SceneIsolation);
            Check(results, "rgb_geometry_occlusion_and_original_robot", ActualRgb);
            Check(results, "billboards_face_each_rendering_camera", Billboards);
            Check(results, "reinitialize_and_owned_resource_cleanup", Cleanup);
            Check(results, "invalid_inputs_fail_explicitly", InvalidInputs);
            results.passed = results.failures == 0;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/sensor-tests.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(results, true));
            if (!results.passed)
            {
                var failures = new List<string>();
                foreach (CaseResult test in results.tests)
                    if (!test.passed) failures.Add(test.name + ": " + test.detail);
                throw new InvalidOperationException("Sensor verification: " + results.failures +
                    " failure(s). See " + path + "\n" + string.Join("\n", failures));
            }
            UnityEngine.Debug.Log("Sensor verification: " + results.tests.Count + " passed. " + path);
            return results;
        }

        private static void Check(Results results, string name, Action test)
        {
            var item = new CaseResult { name = name };
            var timer = Stopwatch.StartNew();
            try { test(); item.passed = true; item.detail = "Behavior verified."; }
            catch (Exception error) { item.detail = error.ToString(); results.failures++; }
            finally { timer.Stop(); item.milliseconds = timer.Elapsed.TotalMilliseconds; results.tests.Add(item); }
        }

        // Returning a farther hit, or treating every hit as free, must fail here.
        private static void Occlusion()
        {
            using (var f = new Fixture())
            {
                BoxCollider front = f.Box(new Vector3(0, .92f, 3), new Vector3(8, 2, .5f));
                f.Box(new Vector3(0, .92f, 6), new Vector3(20, 2, .5f));
                NavRay[] rays = f.Sensor.CaptureDepth();
                Require(rays.Length == 61, "Expected 61 depth samples.");
                foreach (NavRay ray in rays)
                    Require(ray.hit && Math.Abs(ray.ez - 2750) <= 1,
                        "Endpoint crossed the nearest wall instead of stopping on its surface.");
                Require(rays[30].ex == 0 && rays[30].ez == 2750, "Central ray is not axial.");
                front.enabled = false;
                NavRay revealed = f.Sensor.CaptureDepth()[30];
                Require(revealed.hit && revealed.ez == 5750, "Removing an occluder did not reveal geometry.");
            }
        }

        private static void Behind()
        {
            using (var f = new Fixture())
            {
                f.Box(new Vector3(0, .92f, -2), new Vector3(30, 3, .5f));
                foreach (NavRay ray in f.Sensor.CaptureDepth())
                    Require(!ray.hit && ray.ez > 0, "A surface behind the camera was observed.");
            }
        }

        private static void Range()
        {
            using (var f = new Fixture())
            {
                BoxCollider wall = f.Box(new Vector3(0, .92f, 10.2f), new Vector3(30, 2, .2f));
                foreach (NavRay ray in f.Sensor.CaptureDepth())
                {
                    Require(!ray.hit, "A surface beyond ten metres was observed.");
                    double dx = ray.ex - ray.sx, dz = ray.ez - ray.sz;
                    Require(Math.Abs(Math.Sqrt(dx * dx + dz * dz) - 10000) <= 1,
                        "A free endpoint is not ten metres from the eye.");
                }
                wall.transform.position = new Vector3(0, .92f, 10.12f);
                Require(!f.Sensor.CaptureDepth()[30].hit, "Range was measured from the near plane.");
                wall.transform.position = new Vector3(0, .92f, 10.05f);
                NavRay inside = f.Sensor.CaptureDepth()[30];
                Require(inside.hit && Math.Abs(inside.ez - 9950) <= 1, "A surface inside range was missed.");
            }
        }

        private static void OriginAndFrustum()
        {
            using (var f = new Fixture())
            {
                f.Robot.position = new Vector3(-2.375f, .4f, 3.125f);
                f.Robot.rotation = Quaternion.Euler(17, 123, 11);
                f.Sensor.SetYaw(0);
                NavRay[] rays = f.Sensor.CaptureDepth();
                Vector3 eye = f.Sensor.View.transform.position;
                Require(Vector3.Distance(eye, new Vector3(-2.375f, 1.32f, 3.125f)) < .0001f,
                    "Eye must share root XZ and sit .92m above it, independent of body tilt.");
                Require(rays[30].ex == -2375 && rays[30].ez == 13125,
                    "Centre endpoint does not use the odometry frame.");
                float horizontal = Camera.VerticalToHorizontalFieldOfView(
                    f.Sensor.View.fieldOfView, f.Sensor.View.aspect);
                Require(Mathf.Abs(horizontal - 100) < .01f && !f.Sensor.View.orthographic,
                    "RGB camera must use the requested horizontal perspective FOV.");
                Require(Mathf.Abs(f.Sensor.View.aspect - 384f / 216f) < .0001f &&
                    Mathf.Abs(f.Sensor.View.nearClipPlane - .05f) < .0001f,
                    "Camera aspect or near clip differs from the sensor contract.");
                for (int i = 0; i < rays.Length; i++)
                {
                    NavRay ray = rays[i];
                    Require(ray.sx == -2375 && ray.sz == 3125, "Ray starts at a near-plane/offset origin.");
                    // Independent oracle: the actual camera's projection, not an angular ray fan.
                    float u = (.5f + 383f * i / 60f) / 384f;
                    Vector3 direction = f.Sensor.View.ViewportPointToRay(new Vector3(u, .5f, 0)).direction;
                    Vector3 expected = eye + direction * 10;
                    Require(Math.Abs(ray.ex - Mathf.RoundToInt(expected.x * 1000)) <= 1 &&
                        Math.Abs(ray.ez - Mathf.RoundToInt(expected.z * 1000)) <= 1,
                        "Depth sample is not a centre-row pixel of the RGB frustum.");
                }
            }
        }

        private static void RotatedFieldOfView()
        {
            using (var f = new Fixture())
            {
                f.Box(new Vector3(0, .92f, 4), new Vector3(.5f, 2, .5f));
                f.Box(new Vector3(4, .92f, 0), new Vector3(.5f, 2, .5f));
                Require(f.Sensor.CaptureDepth()[30].ez == 3750, "Initial view does not face +z.");
                f.Sensor.SetYaw(450);
                NavRay east = f.Sensor.CaptureDepth()[30];
                Require(f.Sensor.Yaw == 90 && east.hit && east.ex == 3750 && east.ez == 0,
                    "Absolute yaw 90 does not face +x.");
                f.Robot.rotation = Quaternion.Euler(0, 203, 0);
                east = f.Sensor.CaptureDepth()[30];
                Require(east.ex == 3750 && east.ez == 0, "Body yaw changed absolute sensor yaw.");
                f.Sensor.SetYaw(-90);
                NavRay west = f.Sensor.CaptureDepth()[30];
                Require(f.Sensor.Yaw == 270 && !west.hit && west.ex == -10000 && west.ez == 0,
                    "Negative yaw was not normalized to the odometry frame.");
                f.Sensor.SetYaw(359.8f);
                Require(f.Sensor.Yaw == 0, "Rounded yaw must remain within 0..359.");
            }
        }

        private static void OutsideFieldOfView()
        {
            using (var f = new Fixture())
            {
                BoxCollider wall = f.Box(new Vector3(4, .92f, 2), new Vector3(.4f, 2, .4f));
                foreach (NavRay ray in f.Sensor.CaptureDepth()) Require(!ray.hit, "Observed beyond horizontal FOV.");
                wall.transform.position = new Vector3(2, .92f, 2);
                bool seen = false;
                foreach (NavRay ray in f.Sensor.CaptureDepth()) seen |= ray.hit;
                Require(seen, "Missed a surface inside the edge of the frustum.");
            }
        }

        private static void OwnAndLayers()
        {
            using (var f = new Fixture())
            {
                // Saturate small multi-hit buffers with self hits in front of the eye.
                for (int i = 0; i < 80; i++)
                {
                    BoxCollider own = f.Box(new Vector3(0, .92f, .1f + i * .02f), new Vector3(.3f, .3f, .005f));
                    own.transform.SetParent(f.Robot, true);
                }
                f.Box(new Vector3(0, .92f, 2), Vector3.one, 0);
                f.Box(new Vector3(0, .92f, 3), Vector3.one, 9);
                f.Box(new Vector3(0, .92f, 4), new Vector3(1, 1, .5f)).isTrigger = true;
                f.Box(new Vector3(0, .92f, 5), new Vector3(1, 1, .5f));
                NavRay ray = f.Sensor.CaptureDepth()[30];
                Require(ray.hit && ray.ez == 4750, "Self, a nonworld layer, or a trigger occluded depth.");
                foreach (Collider own in f.Robot.GetComponentsInChildren<Collider>())
                    Require(own.enabled && own.gameObject.layer == 8, "Depth capture changed robot physics.");
            }
        }

        private static void NoMapOrSemantics()
        {
            using (var f = new Fixture())
            {
                // No world component, map or scene metadata provider exists in this scene.
                BoxCollider physical = f.Box(new Vector3(0, .92f, 3), new Vector3(4, 2, .5f));
                NavRay[] before = f.Sensor.CaptureDepth();
                physical.name = "free unoccupied goal charger";
                physical.tag = "Player";
                f.Robot.name = "wall hidden-map occupied";
                NavRay[] after = f.Sensor.CaptureDepth();
                for (int i = 0; i < before.Length; i++)
                    Require(JsonUtility.ToJson(before[i]) == JsonUtility.ToJson(after[i]),
                        "Names or tags changed a geometric measurement.");
                physical.enabled = false;
                foreach (NavRay ray in f.Sensor.CaptureDepth())
                    Require(!ray.hit, "Nonphysical metadata was reported as a surface.");
            }
        }

        private static void SceneIsolation()
        {
            using (var a = new Fixture())
            using (var b = new Fixture())
            {
                a.Box(new Vector3(0, .92f, 2), new Vector3(4, 2, .5f));
                Require(a.Sensor.CaptureDepth()[30].hit, "Fixture physics is not queryable.");
                Require(!b.Sensor.CaptureDepth()[30].hit, "A sensor queried another scene's physics.");
            }
        }

        // Color changes must come from rendered geometry, and external cameras must still see the robot.
        private static void ActualRgb()
        {
            using (var f = new Fixture())
            {
                GameObject near = f.VisibleBox(new Vector3(0, .92f, 3), new Vector3(8, 4, .5f), Color.red);
                f.VisibleBox(new Vector3(0, .92f, 6), new Vector3(16, 8, .5f), Color.blue);
                GameObject own = f.VisibleBox(new Vector3(0, .92f, .7f), new Vector3(1, 1, .2f), Color.green);
                own.transform.SetParent(f.Robot, true);
                Material originalMaterial = own.GetComponent<Renderer>().sharedMaterial;
                RenderTexture previousTarget = RenderTexture.active;
                f.Sensor.CaptureDepth();
                Require(RenderTexture.active == previousTarget, "Capture changed the caller's active render texture.");
                Require(f.Sensor.Image && f.Sensor.Image.IsCreated() && f.Sensor.Image.width == 384 &&
                    f.Sensor.Image.height == 216 && f.Sensor.View.targetTexture == f.Sensor.Image,
                    "RGB preview is not backed by the real camera's 384x216 render texture.");
                RequireColor(ReadCentre(f.Sensor.Image), Color.red, "RGB did not show the nearest physical wall.");
                Require(own.layer == 8 && own.GetComponent<Renderer>().enabled &&
                    own.GetComponent<Renderer>().sharedMaterial == originalMaterial,
                    "Sensor rendering modified the original robot.");
                Camera observer = f.Camera();
                observer.CopyFrom(f.Sensor.View);
                observer.cullingMask = ~0;
                observer.scene = f.Scene;
                observer.transform.SetPositionAndRotation(f.Sensor.View.transform.position, f.Sensor.View.transform.rotation);
                observer.Render();
                RequireColor(ReadCentre(f.Sensor.Image), Color.green, "Other cameras lost the original robot.");
                f.Sensor.View.Render();
                RequireColor(ReadCentre(f.Sensor.Image), Color.red, "Direct sensor camera rendering failed to exclude self.");
                near.SetActive(false);
                f.Sensor.CaptureDepth();
                RequireColor(ReadCentre(f.Sensor.Image), Color.blue, "RGB did not reveal the farther geometry.");
                Require(own.layer == 8, "Rendering left the robot on the sensor culling layer.");
            }
        }

        private static void Billboards()
        {
            using (var f = new Fixture())
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.SetParent(f.Root, false);
                quad.transform.position = new Vector3(0, .92f, 3);
                Quaternion original = Quaternion.Euler(20, 75, 0);
                quad.transform.rotation = original;
                quad.transform.localScale = Vector3.one * 2;
                quad.GetComponent<Renderer>().sharedMaterial = f.Material(Color.red);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.AddComponent<CameraFacing>();
                f.Sensor.View.Render();
                RequireColor(ReadCentre(f.Sensor.Image), Color.red, "Billboard did not face the robot camera.");
                RequireColor(ReadPixel(f.Sensor.Image, .6f), Color.red,
                    "A tilted billboard covered the centre pixel but did not face the camera across its width.");
                Require(Quaternion.Angle(quad.transform.rotation, original) < .01f,
                    "Rendering permanently rotated the billboard.");
                Camera side = f.Camera();
                side.CopyFrom(f.Sensor.View);
                side.scene = f.Scene;
                side.transform.SetPositionAndRotation(new Vector3(0, .92f, 6), Quaternion.Euler(0, 180, 0));
                side.Render();
                RequireColor(ReadCentre(f.Sensor.Image), Color.red, "Billboard did not face the second camera.");
                Require(Quaternion.Angle(quad.transform.rotation, original) < .01f,
                    "Second camera left a persistent billboard rotation.");
            }
        }

        private static void Cleanup()
        {
            using (var f = new Fixture())
            {
                Camera first = f.Sensor.View;
                RenderTexture firstImage = f.Sensor.Image;
                f.Sensor.Initialize(f.Robot);
                Require(f.Sensor.View == first && f.Sensor.Image == firstImage, "Repeated initialization leaked resources.");
                Transform second = new GameObject("Replacement robot").transform;
                second.SetParent(f.Root, false);
                f.Sensor.Initialize(second);
                Require(!first && !firstImage && f.Robot, "Retargeting did not release owned resources safely.");
                Camera last = f.Sensor.View;
                GameObject cameraObject = last.gameObject;
                RenderTexture lastImage = f.Sensor.Image;
                f.Sensor.CaptureDepth();
                Object.DestroyImmediate(f.Sensor);
                Require(!last && !cameraObject, "Destroying the sensor leaked its owned camera.");
                Require(!lastImage, "Destroying the sensor leaked its owned render texture.");
                Require(second && f.Robot, "Destroying the sensor destroyed a robot.");
            }
        }

        private static void InvalidInputs()
        {
            using (var f = new Fixture())
            {
                bool rejected = false;
                try { f.Sensor.Initialize(null); } catch (ArgumentNullException) { rejected = true; }
                Require(rejected, "Missing robot was accepted.");
                foreach (float yaw in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    rejected = false;
                    try { f.Sensor.SetYaw(yaw); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Require(rejected, "Nonfinite camera yaw was accepted.");
                }
                Require(f.Sensor.View && f.Sensor.Image && f.Sensor.Yaw == 0,
                    "Rejected input damaged a working sensor.");
            }
        }

        private static Color ReadCentre(RenderTexture target)
        {
            return ReadPixel(target, .5f);
        }

        private static Color ReadPixel(RenderTexture target, float u)
        {
            RenderTexture previous = RenderTexture.active;
            var sample = new Texture2D(1, 1, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                sample.ReadPixels(new Rect(Mathf.FloorToInt(target.width * u), target.height / 2, 1, 1), 0, 0);
                sample.Apply();
                return sample.GetPixel(0, 0);
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(sample); }
        }

        private static void RequireColor(Color actual, Color expected, string message)
        {
            Require(Mathf.Abs(actual.r - expected.r) < .15f && Mathf.Abs(actual.g - expected.g) < .15f &&
                Mathf.Abs(actual.b - expected.b) < .15f, message + " Actual: " + actual);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly Transform Root;
            public readonly Transform Robot;
            public readonly RobotSensor Sensor;
            private readonly List<Material> materials = new List<Material>();

            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    Require(Scene.GetPhysicsScene().IsValid() && Scene.GetPhysicsScene() != Physics.defaultPhysicsScene,
                        "Sensor fixtures require isolated preview physics.");
                    Root = new GameObject("Sensor verification fixtures").transform;
                    SceneManager.MoveGameObjectToScene(Root.gameObject, Scene);
                    Robot = new GameObject("Robot").transform;
                    Robot.SetParent(Root, false);
                    Sensor = Robot.gameObject.AddComponent<RobotSensor>();
                    Sensor.Initialize(Robot);
                    Sensor.SetYaw(0);
                }
                catch { Dispose(); throw; }
            }

            public BoxCollider Box(Vector3 position, Vector3 size, int layer = 8)
            {
                var item = new GameObject("Physical fixture");
                item.transform.SetParent(Root, false);
                item.transform.position = position;
                item.layer = layer;
                var collider = item.AddComponent<BoxCollider>();
                collider.size = size;
                return collider;
            }

            public Material Material(Color color)
            {
                Shader shader = Shader.Find("Unlit/Color");
                Require(shader, "The built-in unlit verification shader is unavailable.");
                var material = new Material(shader) { color = color };
                materials.Add(material);
                return material;
            }

            public GameObject VisibleBox(Vector3 position, Vector3 size, Color color)
            {
                GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
                item.transform.SetParent(Root, false);
                item.transform.position = position;
                item.transform.localScale = size;
                item.layer = 8;
                item.GetComponent<Renderer>().sharedMaterial = Material(color);
                return item;
            }

            public Camera Camera()
            {
                var item = new GameObject("Verification camera");
                item.transform.SetParent(Root, false);
                Camera camera = item.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = Scene;
                return camera;
            }

            public void Dispose()
            {
                if (Root) Object.DestroyImmediate(Root.gameObject);
                foreach (Material material in materials) if (material) Object.DestroyImmediate(material);
                if (Scene.IsValid() && Scene.isLoaded) EditorSceneManager.ClosePreviewScene(Scene);
                Physics.SyncTransforms();
            }
        }
    }
}
#endif
