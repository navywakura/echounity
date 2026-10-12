using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    /// <summary>
    /// Original procedural station biped, built from Unity's shared primitive meshes.
    /// Foot anchors live in world space; gait advances from measured root displacement.
    /// All references needed after saving/reloading an edit-time build are serialized.
    /// </summary>
    // ExecuteAlways enables the destruction callback for an edit-time procedural
    // build. LateUpdate remains play-only; editing a scene never advances the gait.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class RobotVisual : MonoBehaviour
    {
        [Serializable]
        private sealed class Leg
        {
            public float side;
            public Transform hip, knee, thigh, shin, foot;
            [NonSerialized] public Vector3 planted, from, landing, normal;
            [NonSerialized] public Quaternion fromRotation, landingRotation;
            [NonSerialized] public float progress, duration;
            [NonSerialized] public bool swinging;
        }

        [SerializeField] private Transform body, head;
        [SerializeField] private Transform leftShoulder, rightShoulder, leftElbow, rightElbow;
        [SerializeField] private Leg[] legs;
        [SerializeField] private Material[] ownedMaterials;
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private PredictiveMotor motor;
        private Transform motionRoot;
        private Vector3 lastPosition, filteredVelocity;
        private float stridePhase;
        private int nextFoot;
        private bool poseInitialized;
        [NonSerialized] private bool materialsBuiltHere;
        private const float UpperLeg = .26f;
        private const float LowerLeg = .27f;
        private const float AnkleHeight = .105f;

        /// <summary>
        /// Builds safely in edit or play mode. Repeated calls for the same parent return
        /// its existing visual. Materials may subsequently be persisted by the scene builder.
        /// </summary>
        public static RobotVisual Build(Transform parent)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            for (int i = 0; i < parent.childCount; i++)
            {
                RobotVisual existing = parent.GetChild(i).GetComponent<RobotVisual>();
                if (existing) return existing;
            }
            var root = new GameObject("ECHO original station biped");
            root.transform.SetParent(parent, false);
            RobotVisual visual = root.AddComponent<RobotVisual>();
            try
            {
                visual.Construct();
                visual.InitializePose();
                visual.PoseLegs();
                return visual;
            }
            catch
            {
                DestroyOwned(root);
                throw;
            }
        }

        private void Construct()
        {
            Shader shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("ECHO robot requires the built-in Standard shader.");
            ownedMaterials = new Material[7];
            materialsBuiltHere = true;
            ownedMaterials[0] = MakeMaterial(shader, "Porcelain shell", new Color(.82f, .89f, .90f), .35f, .58f);
            ownedMaterials[1] = MakeMaterial(shader, "Midnight enamel", new Color(.055f, .13f, .19f), .50f, .55f);
            ownedMaterials[2] = MakeMaterial(shader, "Graphite joints", new Color(.045f, .062f, .072f), .25f, .32f);
            ownedMaterials[3] = MakeMaterial(shader, "Warm copper trim", new Color(.93f, .39f, .14f), .60f, .48f);
            ownedMaterials[4] = MakeMaterial(shader, "Cyan optical emission", new Color(.12f, .9f, 1f), .20f, .75f);
            ownedMaterials[4].EnableKeyword("_EMISSION");
            ownedMaterials[4].SetColor("_EmissionColor", new Color(.12f, .95f, 1f) * 2.2f);
            ownedMaterials[5] = MakeMaterial(shader, "Obsidian floating visor", new Color(.008f, .025f, .042f), .65f, .90f);
            ownedMaterials[6] = MakeMaterial(shader, "Brushed alloy", new Color(.32f, .43f, .48f), .85f, .42f);

            body = Pivot("Suspended body", transform, Vector3.zero);
            Piece("Rounded torso", PrimitiveType.Sphere, body, new Vector3(0, .82f, 0), new Vector3(.47f, .43f, .35f), 0);
            Piece("Lower torso band", PrimitiveType.Sphere, body, new Vector3(0, .68f, 0), new Vector3(.35f, .17f, .28f), 1);
            Piece("Pelvis", PrimitiveType.Sphere, body, new Vector3(0, .58f, 0), new Vector3(.32f, .15f, .25f), 2);
            Piece("Chest inset", PrimitiveType.Sphere, body, new Vector3(0, .82f, .153f), new Vector3(.25f, .22f, .07f), 1);
            Piece("Chest copper bezel", PrimitiveType.Sphere, body, new Vector3(-.055f, .85f, .185f), new Vector3(.10f, .10f, .026f), 3);
            Piece("Chest status lens", PrimitiveType.Sphere, body, new Vector3(-.055f, .85f, .202f), new Vector3(.058f, .058f, .018f), 4);
            for (int i = 0; i < 3; i++)
                Piece("Chest vent " + i, PrimitiveType.Cube, body, new Vector3(.057f, .795f + i * .024f, .185f),
                    new Vector3(.055f, .008f, .018f), 6);
            Piece("Backpack", PrimitiveType.Sphere, body, new Vector3(0, .81f, -.205f), new Vector3(.29f, .32f, .18f), 1);
            Piece("Backpack copper spine", PrimitiveType.Capsule, body, new Vector3(0, .81f, -.294f), new Vector3(.046f, .11f, .025f), 3);
            Piece("Left battery", PrimitiveType.Capsule, body, new Vector3(-.105f, .81f, -.252f), new Vector3(.068f, .105f, .068f), 6);
            Piece("Right battery", PrimitiveType.Capsule, body, new Vector3(.105f, .81f, -.252f), new Vector3(.068f, .105f, .068f), 6);
            Piece("Neck bearing", PrimitiveType.Sphere, body, new Vector3(0, 1.02f, 0), new Vector3(.16f, .10f, .16f), 2);

            head = Pivot("Optical head", body, new Vector3(0, 1.135f, 0));
            Piece("Rounded head shell", PrimitiveType.Sphere, head, Vector3.zero, new Vector3(.43f, .30f, .33f), 0);
            Piece("Floating face visor", PrimitiveType.Sphere, head, new Vector3(0, 0, .154f), new Vector3(.385f, .185f, .11f), 5);
            Piece("Left cyan eye", PrimitiveType.Sphere, head, new Vector3(-.088f, .005f, .204f), new Vector3(.067f, .083f, .026f), 4);
            Piece("Right cyan eye", PrimitiveType.Sphere, head, new Vector3(.088f, .005f, .204f), new Vector3(.067f, .083f, .026f), 4);
            Piece("Brow copper pin", PrimitiveType.Sphere, head, new Vector3(.16f, .075f, .08f), new Vector3(.038f, .038f, .03f), 3);
            Piece("Left temple bearing", PrimitiveType.Sphere, head, new Vector3(-.21f, 0, 0), new Vector3(.055f, .095f, .095f), 1);
            Piece("Right temple bearing", PrimitiveType.Sphere, head, new Vector3(.21f, 0, 0), new Vector3(.055f, .095f, .095f), 1);
            Piece("Antenna mount", PrimitiveType.Sphere, body, new Vector3(-.145f, 1.14f, -.13f), new Vector3(.066f, .066f, .066f), 3);
            Piece("Antenna", PrimitiveType.Capsule, body, new Vector3(-.145f, 1.24f, -.13f), new Vector3(.018f, .085f, .018f), 6);
            Piece("Antenna beacon", PrimitiveType.Sphere, body, new Vector3(-.145f, 1.325f, -.13f), Vector3.one * .04f, 4);

            BuildArm(-1, out leftShoulder, out leftElbow);
            BuildArm(1, out rightShoulder, out rightElbow);
            legs = new[] { BuildLeg(-1), BuildLeg(1) };
        }

        private void BuildArm(float side, out Transform shoulder, out Transform elbow)
        {
            string prefix = side < 0 ? "Left" : "Right";
            shoulder = Pivot(prefix + " shoulder pivot", body, new Vector3(side * .235f, .91f, 0));
            Piece(prefix + " shoulder joint", PrimitiveType.Sphere, shoulder, Vector3.zero, Vector3.one * .135f, 2);
            Piece(prefix + " shoulder shell", PrimitiveType.Sphere, shoulder, new Vector3(side * .025f, -.035f, 0), new Vector3(.15f, .15f, .17f), 0);
            Piece(prefix + " upper arm", PrimitiveType.Capsule, shoulder, new Vector3(side * .025f, -.12f, 0), new Vector3(.09f, .095f, .09f), 1);
            elbow = Pivot(prefix + " elbow pivot", shoulder, new Vector3(side * .025f, -.215f, 0));
            Piece(prefix + " elbow bearing", PrimitiveType.Sphere, elbow, Vector3.zero, Vector3.one * .085f, 3);
            Piece(prefix + " forearm", PrimitiveType.Capsule, elbow, new Vector3(0, -.075f, 0), new Vector3(.105f, .083f, .105f), 0);
            Piece(prefix + " gripper", PrimitiveType.Sphere, elbow, new Vector3(0, -.168f, .012f), new Vector3(.105f, .083f, .10f), 2);
            Piece(prefix + " gripper thumb", PrimitiveType.Sphere, elbow, new Vector3(-side * .045f, -.145f, .035f), new Vector3(.035f, .065f, .042f), 6);
        }

        private Leg BuildLeg(float side)
        {
            string prefix = side < 0 ? "Left" : "Right";
            var leg = new Leg { side = side };
            leg.hip = Pivot(prefix + " hip", body, new Vector3(side * .125f, .565f, 0));
            Piece(prefix + " hip bearing", PrimitiveType.Sphere, leg.hip, Vector3.zero, Vector3.one * .12f, 6);
            leg.knee = Piece(prefix + " knee bearing", PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * .105f, 3);
            leg.thigh = Piece(prefix + " thigh", PrimitiveType.Capsule, transform, Vector3.zero, Vector3.one, 1);
            leg.shin = Piece(prefix + " shin", PrimitiveType.Capsule, transform, Vector3.zero, Vector3.one, 0);
            leg.foot = Pivot(prefix + " Foot", transform, new Vector3(side * .125f, 0, .01f));
            Piece(prefix + " ankle bearing", PrimitiveType.Sphere, leg.foot, Vector3.up * AnkleHeight, Vector3.one * .085f, 2);
            Piece(prefix + " boot", PrimitiveType.Sphere, leg.foot, new Vector3(0, .068f, .028f), new Vector3(.185f, .136f, .285f), 1);
            Piece(prefix + " sole", PrimitiveType.Cube, leg.foot, new Vector3(0, .02f, .025f), new Vector3(.16f, .04f, .23f), 2);
            Piece(prefix + " toe cap", PrimitiveType.Sphere, leg.foot, new Vector3(0, .06f, .108f), new Vector3(.155f, .085f, .09f), 0);
            Piece(prefix + " boot light", PrimitiveType.Sphere, leg.foot, new Vector3(0, .071f, .151f), new Vector3(.052f, .017f, .012f), 4);
            return leg;
        }

        private static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            return item.transform;
        }

        private Transform Piece(string name, PrimitiveType shape, Transform parent, Vector3 position, Vector3 scale, int material)
        {
            GameObject item = GameObject.CreatePrimitive(shape);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            Collider collider = item.GetComponent<Collider>();
            if (collider) { collider.enabled = false; DestroyOwned(collider); }
            Renderer renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = ownedMaterials[material];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return item.transform;
        }

        private static Material MakeMaterial(Shader shader, string name, Color color, float metal, float gloss)
        {
            var material = new Material(shader) { name = "ECHO / " + name, color = color };
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Glossiness", gloss);
            return material;
        }

        private void OnEnable() { poseInitialized = false; }
        private void LateUpdate() { if (Application.isPlaying) Tick(Time.deltaTime); }

        private void InitializePose()
        {
            if (poseInitialized || !body || !head || legs == null || legs.Length != 2) return;
            motionRoot = transform.parent ? transform.parent : transform;
            motor = GetComponentInParent<PredictiveMotor>();
            lastPosition = motionRoot.position;
            filteredVelocity = Vector3.zero;
            foreach (Leg leg in legs)
            {
                Vector3 normal;
                leg.planted = GroundPoint(leg.foot.position, out normal);
                leg.normal = normal;
                leg.foot.position = leg.planted;
                leg.foot.rotation = FootRotation(normal);
                leg.swinging = false;
                leg.progress = 0;
            }
            poseInitialized = true;
        }

        /// <summary>Optional deterministic pose step for edit-mode verification.</summary>
        public void Tick(float dt)
        {
            if (!Finite(dt) || dt <= 0f) return;
            InitializePose();
            if (!poseInitialized || !isActiveAndEnabled) return;
            dt = Mathf.Min(dt, .05f);
            Vector3 position = motionRoot.position;
            Vector3 delta = position - lastPosition;
            delta.y = 0;
            lastPosition = position;
            if (!Finite(delta)) return;
            // External scene placement may change the root discontinuously. Replant the
            // visual only; animation never writes the motor/root transform.
            if (delta.sqrMagnitude > .64f) { poseInitialized = false; InitializePose(); PoseLegs(); return; }
            Vector3 measured = delta / dt;
            float blend = 1f - Mathf.Exp(-12f * dt);
            filteredVelocity = Vector3.Lerp(filteredVelocity, measured, blend);
            float speed = measured.magnitude;
            stridePhase += delta.magnitude * 10f;
            stridePhase %= Mathf.PI * 2f;
            if (!motor) motor = GetComponentInParent<PredictiveMotor>();

            Vector3 localVelocity = motionRoot.InverseTransformDirection(filteredVelocity);
            float bob = Mathf.Min(filteredVelocity.magnitude / 1.65f, 1f) *
                (.009f + .008f * Mathf.Cos(stridePhase * 2f));
            body.localPosition = Vector3.up * bob;
            Quaternion lean = Quaternion.Euler(Mathf.Clamp(localVelocity.z * 3f, -5, 5), 0,
                Mathf.Clamp(-localVelocity.x * 4f, -6, 6));
            body.localRotation = Quaternion.Slerp(body.localRotation, lean, blend);

            bool busy = legs[0].swinging || legs[1].swinging;
            if (!busy)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int index = (nextFoot + attempt) % 2;
                    Leg leg = legs[index];
                    Vector3 rest = motionRoot.TransformPoint(new Vector3(leg.side * .125f, 0, .01f));
                    Vector3 error = rest - leg.planted;
                    error.y = 0;
                    if (error.magnitude <= (speed > .08f ? .08f : .07f)) continue;
                    leg.from = leg.planted;
                    leg.duration = Mathf.Clamp(.17f / Mathf.Max(.45f, speed), .105f, .32f);
                    Vector3 lead = Vector3.ClampMagnitude(filteredVelocity * leg.duration * .9f, .18f);
                    leg.landing = GroundPoint(rest + lead, out leg.normal);
                    leg.fromRotation = leg.foot.rotation;
                    leg.landingRotation = FootRotation(leg.normal);
                    leg.progress = 0;
                    leg.swinging = true;
                    nextFoot = 1 - index;
                    break;
                }
            }
            foreach (Leg leg in legs)
            {
                if (leg.swinging)
                {
                    // Keep support travel bounded during acceleration as well as at cruise.
                    float duration = Mathf.Min(leg.duration, Mathf.Clamp(.17f / Mathf.Max(.45f, speed), .105f, .32f));
                    leg.progress = Mathf.Min(1f, leg.progress + dt / duration);
                    float t = leg.progress;
                    float eased = t * t * (3f - 2f * t);
                    leg.foot.position = Vector3.Lerp(leg.from, leg.landing, eased) +
                        Vector3.up * (Mathf.Sin(t * Mathf.PI) * .085f);
                    leg.foot.rotation = Quaternion.Slerp(leg.fromRotation, leg.landingRotation, eased);
                    if (t >= 1f) { leg.planted = leg.landing; leg.swinging = false; }
                }
                else leg.foot.position = leg.planted;
            }
            PoseLegs();
            float swing = Mathf.Sin(stridePhase) * Mathf.Clamp(filteredVelocity.magnitude * 15f, 0, 25);
            leftShoulder.localRotation = Quaternion.Slerp(leftShoulder.localRotation, Quaternion.Euler(swing, 0, -7), blend);
            rightShoulder.localRotation = Quaternion.Slerp(rightShoulder.localRotation, Quaternion.Euler(-swing, 0, 7), blend);
            leftElbow.localRotation = Quaternion.Euler(-12f - Mathf.Abs(swing) * .3f, 0, 0);
            rightElbow.localRotation = leftElbow.localRotation;
            UpdateLook(blend);
        }

        private void PoseLegs()
        {
            foreach (Leg leg in legs)
            {
                Vector3 hip = leg.hip.position;
                Vector3 ankle = leg.foot.position + leg.foot.up * AnkleHeight;
                Vector3 offset = ankle - hip;
                float rawDistance = offset.magnitude;
                Vector3 axis = rawDistance > .0001f ? offset / rawDistance : Vector3.down;
                float distance = Mathf.Clamp(rawDistance, Mathf.Abs(UpperLeg - LowerLeg) + .001f,
                    UpperLeg + LowerLeg - .001f);
                float along = (UpperLeg * UpperLeg - LowerLeg * LowerLeg + distance * distance) / (2f * distance);
                float bendHeight = Mathf.Sqrt(Mathf.Max(0f, UpperLeg * UpperLeg - along * along));
                Vector3 pole = Vector3.ProjectOnPlane(motionRoot.forward + motionRoot.right * (leg.side * .12f), axis);
                if (pole.sqrMagnitude < .0001f) pole = Vector3.ProjectOnPlane(motionRoot.up, axis);
                if (pole.sqrMagnitude < .0001f) pole = motionRoot.right;
                Vector3 knee = hip + axis * along + pole.normalized * bendHeight;
                leg.knee.position = knee;
                PlaceBone(leg.thigh, hip, knee, .115f);
                PlaceBone(leg.shin, knee, ankle, .125f);
            }
        }

        private static void PlaceBone(Transform bone, Vector3 start, Vector3 end, float width)
        {
            Vector3 delta = end - start;
            bone.position = (start + end) * .5f;
            if (delta.sqrMagnitude > .0000001f) bone.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            // Built-in capsules are 2 units tall and one unit across.
            bone.localScale = new Vector3(width, Mathf.Max(.001f, delta.magnitude * .5f), width);
        }

        private Vector3 GroundPoint(Vector3 point, out Vector3 normal)
        {
            normal = Vector3.up;
            PhysicsScene scene = gameObject.scene.GetPhysicsScene();
            Vector3 origin = new Vector3(point.x, motionRoot.position.y + .6f, point.z);
            int count = scene.Raycast(origin, Vector3.down, groundHits, 1.3f, 1 << 9, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Vector3 result = new Vector3(point.x, motionRoot.position.y, point.z);
            if (count == groundHits.Length) return result;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (!hit.collider || hit.transform == motionRoot || hit.transform.IsChildOf(motionRoot) || hit.normal.y < .5f) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                result = hit.point;
                normal = hit.normal;
            }
            return result;
        }

        private Quaternion FootRotation(Vector3 normal)
        {
            Vector3 forward = Vector3.ProjectOnPlane(motionRoot.forward, normal);
            return Quaternion.LookRotation(forward.sqrMagnitude > .0001f ? forward : Vector3.forward, normal);
        }

        private void UpdateLook(float blend)
        {
            Vector3 look = motor ? motor.DesiredLookPoint : motionRoot.position + motionRoot.forward * 2 + Vector3.up;
            Vector3 direction = body.InverseTransformDirection(look - head.position);
            if (!Finite(direction) || direction.sqrMagnitude < .0001f) return;
            float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -55f, 55f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(direction.y,
                Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z)) * Mathf.Rad2Deg, -22f, 22f);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitch, yaw, 0), blend);
        }

        private void OnDestroy()
        {
            // Scene-loaded materials are shared serialized resources. Only a Build in
            // this instance's lifetime creates allocations for this instance to release.
            if (!materialsBuiltHere || ownedMaterials == null) return;
            foreach (Material material in ownedMaterials)
            {
                if (!material) continue;
#if UNITY_EDITOR
                // The parent scene builder persists these materials with AssetDatabase.
                // Assets are shared resources, no longer owned transient allocations.
                if (UnityEditor.EditorUtility.IsPersistent(material)) continue;
#endif
                DestroyOwned(material);
            }
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
        private static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
    }
}
