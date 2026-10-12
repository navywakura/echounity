using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    /// <summary>
    /// A real RGB preview and centre-row geometric depth, sharing one camera frustum.
    /// Depth is physical range plus odometry, not RGB category recognition.
    /// </summary>
    // Edit-time Initialize owns native resources too, so destruction callbacks must run there.
    [ExecuteAlways, DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class RobotSensor : MonoBehaviour
    {
        private const int Width = 384, Height = 216, Samples = 61;
        private const int ObstacleMask = 1 << 8;
        // Reserved only for the duration of this camera's render; never change collision settings.
        private const int SelfRenderLayer = 31;
        private const float EyeHeight = .92f, HorizontalFov = 100f, RangeMetres = 10f;
        public float MountHeight = EyeHeight;

        [NonSerialized] public Camera View;
        public RenderTexture Image { get; private set; }
        public int Yaw { get; private set; }

        private Transform robot;
        private GameObject cameraObject;
        private RaycastHit[] hits = new RaycastHit[32];
        private readonly List<Renderer> ownRenderers = new List<Renderer>();
        private readonly Dictionary<GameObject, int> maskedLayers = new Dictionary<GameObject, int>();
        private bool subscribed;

        public void Initialize(Transform robot)
        {
            if (!robot) throw new ArgumentNullException(nameof(robot));
            if (this.robot == robot && View && Image)
            {
                SynchronizeView();
                return;
            }
            ReleaseResources();
            this.robot = robot;
            try
            {
                cameraObject = new GameObject("ECHO RGB-D camera");
                SceneManager.MoveGameObjectToScene(cameraObject, robot.gameObject.scene);
                cameraObject.transform.SetParent(robot, false);
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                View = cameraObject.AddComponent<Camera>();
                View.enabled = false; // Render explicitly after movement, and at each depth capture.
                View.orthographic = false;
                View.usePhysicalProperties = false;
                View.aspect = Width / (float)Height;
                View.fieldOfView = Camera.HorizontalToVerticalFieldOfView(HorizontalFov, View.aspect);
                View.nearClipPlane = .05f;
                View.farClipPlane = RangeMetres;
                View.cullingMask = ~(1 << SelfRenderLayer);
                View.clearFlags = CameraClearFlags.SolidColor;
                View.backgroundColor = new Color(.035f, .055f, .07f, 1);
                View.allowHDR = false;
                View.allowMSAA = false;
                // Dynamic and preview-scene geometry must not depend on baked occlusion data.
                View.useOcclusionCulling = false;
                Image = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) {
                    name = "ECHO RGB-D image", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, autoGenerateMips = false, antiAliasing = 1
                };
                View.targetTexture = Image;
                Subscribe();
                SetYaw(robot.eulerAngles.y);
            }
            catch { ReleaseResources(); this.robot = null; throw; }
        }

        /// <summary>Absolute odometry yaw, quantized to the same integer sent in the observation.</summary>
        public void SetYaw(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                throw new ArgumentOutOfRangeException(nameof(degrees), "Yaw must be finite.");
            Yaw = Mathf.RoundToInt(Mathf.Repeat(degrees, 360f)) % 360;
            SynchronizeView();
        }

        public NavRay[] CaptureDepth()
        {
            if (!robot || !View || !Image)
                throw new InvalidOperationException("Initialize the sensor with a live robot before capture.");
            SynchronizeView();
            Physics.SyncTransforms();
            PhysicsScene physics = robot.gameObject.scene.GetPhysicsScene();
            if (!physics.IsValid()) throw new InvalidOperationException("Robot has no valid physics scene.");

            Vector3 origin = View.transform.position;
            int sx = Millimetres(origin.x), sz = Millimetres(origin.z);
            var rays = new NavRay[Samples];
            for (int i = 0; i < Samples; i++)
            {
                // Uniform image samples, not uniform angles. Even image height: optical centre row.
                float u = (.5f + (Width - 1f) * i / (Samples - 1f)) / Width;
                Vector3 direction = View.ViewportPointToRay(new Vector3(u, .5f, 0)).direction.normalized;
                // ViewportPointToRay starts at the near plane. Keep odometry and range at the eye.
                int count;
                while (true)
                {
                    count = physics.Raycast(origin, direction, hits, RangeMetres,
                        ObstacleMask, QueryTriggerInteraction.Ignore);
                    if (count < hits.Length) break;
                    // A full buffer can omit the nearest nonself hit; never silently truncate it.
                    Array.Resize(ref hits, checked(hits.Length * 2));
                }
                bool found = false;
                float distance = RangeMetres;
                Vector3 endpoint = origin + direction * RangeMetres;
                for (int j = 0; j < count; j++)
                {
                    Collider collider = hits[j].collider;
                    if (!collider) continue;
                    Transform identity = collider.transform;
                    if (identity == robot || identity.IsChildOf(robot)) continue;
                    if (hits[j].distance > distance) continue;
                    found = true;
                    distance = hits[j].distance;
                    endpoint = hits[j].point;
                }
                rays[i] = new NavRay { sx = sx, sz = sz,
                    ex = Millimetres(endpoint.x), ez = Millimetres(endpoint.z), hit = found };
            }
            RenderImage();
            return rays;
        }

        private static int Millimetres(float metres) { return Mathf.RoundToInt(metres * 1000f); }

        private void SynchronizeView()
        {
            if (!robot || !View) return;
            View.transform.SetPositionAndRotation(robot.position + Vector3.up * Mathf.Clamp(MountHeight,.1f,1.2f),
                Quaternion.Euler(0, Yaw, 0));
#if UNITY_EDITOR
            // Preview cameras must render the same isolated scene their physics query uses.
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(robot.gameObject.scene))
                View.scene = robot.gameObject.scene;
#endif
        }

        private void LateUpdate()
        {
            if (!Application.IsPlaying(gameObject)) return;
            if (!robot) { ReleaseResources(); return; }
            if (View && Image) { SynchronizeView(); RenderImage(); }
        }

        private void RenderImage()
        {
            // PhysX depth rays work without a graphics device. Camera.Render does not.
            // RGB is a passive viewer; a headless check must never call a null renderer.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            { RestoreRobotLayers(); return; }
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (!Image.IsCreated() && !Image.Create())
                    throw new InvalidOperationException("Unable to create the RGB sensor render texture.");
                View.Render();
            }
            finally { RestoreRobotLayers(); RenderTexture.active = previous; }
        }

        private void Subscribe()
        {
            if (subscribed) return;
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
            subscribed = true;
        }

        private void BeforeCamera(Camera camera)
        {
            if (camera != View || !robot) return;
            SynchronizeView();
            // Only this robot's renderer identities are inspected, including new visual children.
            // The original layers, colliders, materials and renderer enable flags survive capture.
            robot.GetComponentsInChildren(true, ownRenderers);
            foreach (Renderer renderer in ownRenderers)
            {
                if (!renderer) continue;
                GameObject item = renderer.gameObject;
                if (maskedLayers.ContainsKey(item)) continue;
                maskedLayers.Add(item, item.layer);
                item.layer = SelfRenderLayer;
            }
        }

        private void AfterCamera(Camera camera)
        {
            if (camera == View) RestoreRobotLayers();
        }

        private void RestoreRobotLayers()
        {
            foreach (KeyValuePair<GameObject, int> item in maskedLayers)
                if (item.Key) item.Key.layer = item.Value;
            maskedLayers.Clear();
            ownRenderers.Clear();
        }

        private void OnEnable()
        {
            if (!robot || View) return;
            int previousYaw = Yaw;
            Initialize(robot);
            SetYaw(previousYaw);
        }

        private void OnDisable() { ReleaseResources(); }
        private void OnDestroy() { ReleaseResources(); robot = null; }

        private void ReleaseResources()
        {
            if (subscribed)
            {
                Camera.onPreCull -= BeforeCamera;
                Camera.onPostRender -= AfterCamera;
                subscribed = false;
            }
            RestoreRobotLayers();
            if (View) { View.enabled = false; View.targetTexture = null; }
            if (Image)
            {
                if (RenderTexture.active == Image) RenderTexture.active = null;
                Image.Release();
                DestroyOwned(Image);
            }
            Image = null;
            if (cameraObject) DestroyOwned(cameraObject);
            cameraObject = null;
            View = null;
            // No cloned materials or robot geometry are owned by the sensor.
        }

        private static void DestroyOwned(UnityEngine.Object item)
        {
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }
    }
}
