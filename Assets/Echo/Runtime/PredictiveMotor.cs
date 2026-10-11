using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    /// <summary>
    /// Bounded local locomotion on the station's level XZ floor. World geometry is
    /// layer 8; ground is layer 9. This is a local avoidance motor, not a pathfinder.
    /// The owner places an upright, unit-scale CharacterController at floor y=0.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class PredictiveMotor : MonoBehaviour
    {
        public const float BodyRadius = .26f;
        public const float BodyHeight = 1.35f;
        public const float MaximumSpeed = 1.65f;
        public const float MaximumTick = .05f;
        public const float MaximumDisplacement = .09f;
        public const int WorldMask = 1 << 8;
        public const int GroundMask = 1 << 9;

        private const float Acceleration = 4.5f;
        private const float Braking = 6.5f;
        private const float ArrivalRadius = .045f;
        private const float Clearance = .012f;
        private const int ChainSegments = 12;
        private const int QueryCapacity = 64;
        private static readonly float[] SteeringAngles = { 0, 28, -28, 55, -55, 80, -80, 100, -100 };

        [SerializeField] private bool showPrediction;
        [SerializeField, HideInInspector] private Transform predictionRoot;
        [SerializeField, HideInInspector] private LineRenderer frontLine, rearLine;
        [SerializeField, HideInInspector] private Transform[] frontDots, rearDots;
        [SerializeField, HideInInspector] private Material predictionMaterial;

        private readonly RaycastHit[] hits = new RaycastHit[QueryCapacity];
        private readonly Collider[] overlaps = new Collider[QueryCapacity];
        private readonly Vector3[] front = new Vector3[ChainSegments + 1];
        private readonly Vector3[] rear = new Vector3[ChainSegments + 1];
        private readonly Vector3[] scratch = new Vector3[ChainSegments + 1];
        private CharacterController controller;
        private PhysicsScene physicsScene;
        private Vector3 target, velocity, steering;
        private Vector3 desiredLookPoint;
        private float bestDistance, noProgressTime, targetTime, timeBudget;
        private float preferredSide;
        private int frontCount, rearCount;
        private bool initialized, hierarchyDirty = true, validateTarget;
        private bool ownsPredictionMaterial;

        public bool HasTarget { get; private set; }
        public bool Arrived { get; private set; }
        public bool Blocked { get; private set; }
        /// <summary>Actual horizontal metres per simulated second, after collision.</summary>
        public float Speed { get; private set; }
        public Vector3 DesiredLookPoint
        {
            get { return desiredLookPoint; }
            set { if (Finite(value)) desiredLookPoint = value; }
        }
        public bool ShowPrediction
        {
            get { return showPrediction; }
            set
            {
                showPrediction = value;
                if (value) { Initialize(); UpdatePredictions(); }
                else if (predictionRoot) predictionRoot.gameObject.SetActive(false);
            }
        }

        private void Awake() { if (Application.IsPlaying(gameObject)) Initialize(); }
        private void OnEnable() { hierarchyDirty = true; }
        private void OnTransformChildrenChanged() { hierarchyDirty = true; }
        private void FixedUpdate() { if (Application.IsPlaying(gameObject)) Tick(Time.fixedDeltaTime); }
        private void OnDisable()
        {
            Stop();
            if (predictionRoot) predictionRoot.gameObject.SetActive(false);
        }

        private void Initialize()
        {
            if (initialized) return;
            controller = GetComponent<CharacterController>();
            if (!controller) return;
            controller.radius = BodyRadius;
            controller.height = BodyHeight;
            controller.center = Vector3.up * (BodyHeight * .5f);
            controller.skinWidth = .008f;
            controller.stepOffset = 0f;
            controller.slopeLimit = 45f;
            controller.minMoveDistance = 0f;
            controller.detectCollisions = true;
            controller.enableOverlapRecovery = false;
            // These collider overrides also constrain the final native Move, independently
            // of the project's layer collision matrix. No global Physics settings change.
            controller.includeLayers = WorldMask | GroundMask;
            controller.excludeLayers = ~(WorldMask | GroundMask);
            controller.layerOverridePriority = 100;
            physicsScene = gameObject.scene.GetPhysicsScene();
            desiredLookPoint = transform.position + transform.forward * 2f;
            steering = Flat(transform.forward).normalized;
            initialized = true;
        }

        public void SetTarget(Vector3 world)
        {
            Initialize();
            if (!Finite(world)) { Finish(false, true); return; }
            world.y = transform.position.y;
            // An owner may refresh the same goal every frame. Such refreshes must not
            // defeat the non-progress budget, or repeatedly restart an arrived goal.
            if ((HasTarget || Arrived || Blocked) && (world - target).sqrMagnitude < .0001f)
                return;
            target = world;
            desiredLookPoint = world + Vector3.up;
            HasTarget = true;
            Arrived = Blocked = false;
            validateTarget = true;
            bestDistance = Flat(target - transform.position).magnitude;
            if (!Finite(bestDistance)) { Finish(false, true); return; }
            noProgressTime = targetTime = 0f;
            timeBudget = Mathf.Clamp(bestDistance / MaximumSpeed * 5f + 8f, 10f, 60f);
            preferredSide = 0f;
        }

        /// <summary>Explicit cancellation is immediate and clears arrival/blockage.</summary>
        public void Stop() { Finish(false, false); }

        private void Finish(bool arrived, bool blocked)
        {
            HasTarget = false;
            Arrived = arrived;
            Blocked = blocked;
            velocity = Vector3.zero;
            Speed = 0f;
        }

        /// <summary>
        /// Deterministic manual step. Invalid dt is inert. Excess time is discarded,
        /// never accumulated into a teleport or an unbounded catch-up loop.
        /// Call Physics.SyncTransforms before Tick after editing colliders in edit mode.
        /// </summary>
        public void Tick(float dt)
        {
            if (!Finite(dt) || dt <= 0f) return;
            Initialize();
            if (!initialized || !isActiveAndEnabled) return;
            dt = Mathf.Min(dt, MaximumTick);
            physicsScene = gameObject.scene.GetPhysicsScene();
            if (!physicsScene.IsValid() || !controller.enabled || !ValidPose())
            {
                Finish(false, true);
                return;
            }
            if (hierarchyDirty) IgnoreOwnColliders();
            if (!HasTarget) { Speed = 0f; velocity = Vector3.zero; UpdatePredictions(); return; }

            Vector3 start = transform.position;
            // Casts do not guarantee reporting an initial penetration. Fail closed:
            // no ComputePenetration, transform rewrites, or overlap recovery are used.
            if (Occupied(start) || (validateTarget && Occupied(target)))
            {
                Finish(false, true);
                UpdatePredictions();
                return;
            }
            validateTarget = false;
            Vector3 offset = Flat(target - start);
            float distance = offset.magnitude;
            if (distance <= ArrivalRadius && velocity.magnitude <= Braking * dt)
            {
                Finish(true, false);
                UpdatePredictions();
                return;
            }

            float horizon = Mathf.Clamp(.55f + velocity.magnitude * .35f +
                velocity.sqrMagnitude / (2f * Braking), .55f, 1.6f);
            float free;
            steering = ChooseSteering(offset / distance, horizon, out free);
            free = Mathf.Min(free, UpdatePredictions(horizon));
            // Include one discrete step in the stopping-distance budget, so entering
            // the arrival radius never needs an instantaneous last-frame brake.
            float brakeStep = Braking * dt;
            float arrivalSpeed = Mathf.Sqrt(brakeStep * brakeStep +
                2f * Braking * Mathf.Max(0f, distance - .03f)) - brakeStep;
            float obstacleSpeed = Mathf.Sqrt(2f * Braking * Mathf.Max(0f, free - Clearance));
            float wantedSpeed = Mathf.Min(MaximumSpeed, Mathf.Min(arrivalSpeed, obstacleSpeed));
            Vector3 wantedVelocity = steering * wantedSpeed;
            float rate = wantedSpeed < velocity.magnitude ? Braking : Acceleration;
            velocity = Vector3.MoveTowards(velocity, wantedVelocity, rate * dt);

            Vector3 displacement = Vector3.ClampMagnitude(velocity * dt,
                Mathf.Min(MaximumDisplacement, Mathf.Max(0f, distance - .025f)));
            SweepAndSlide(displacement);
            Vector3 actual = Flat(transform.position - start);
            velocity = actual / dt;
            Speed = velocity.magnitude;
            Vector3 facing = Speed > .015f ? velocity : steering;
            if (facing.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(facing, Vector3.up), 240f * dt);

            float remaining = Flat(target - transform.position).magnitude;
            targetTime += dt;
            if (remaining < bestDistance - .025f)
            {
                bestDistance = remaining;
                noProgressTime = 0f;
            }
            else noProgressTime += dt;
            if (remaining <= ArrivalRadius && Speed <= .0001f) Finish(true, false);
            else if (noProgressTime >= 3f || targetTime >= timeBudget) Finish(false, true);
            RenderPredictions();
        }

        private bool ValidPose()
        {
            Vector3 scale = transform.lossyScale;
            return Finite(transform.position) && (scale - Vector3.one).sqrMagnitude < .0001f &&
                Vector3.Dot(transform.up, Vector3.up) > .9999f;
        }

        private void IgnoreOwnColliders()
        {
            foreach (Collider item in GetComponentsInChildren<Collider>(true))
                if (item && item != controller && item.enabled && item.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(controller, item, true);
            hierarchyDirty = false;
        }

        private bool IsOwn(Collider item)
        {
            return item && (item.transform == transform || item.transform.IsChildOf(transform));
        }

        private static void Capsule(Vector3 position, out Vector3 lower, out Vector3 upper)
        {
            lower = position + Vector3.up * BodyRadius;
            upper = position + Vector3.up * (BodyHeight - BodyRadius);
        }

        private bool Occupied(Vector3 position)
        {
            Vector3 low, high;
            Capsule(position, out low, out high);
            int count = physicsScene.OverlapCapsule(low, high, BodyRadius - .002f,
                overlaps, WorldMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return true; // Saturated queries never guess free space.
            for (int i = 0; i < count; i++)
                if (overlaps[i] && !IsOwn(overlaps[i])) return true;
            return false;
        }

        private bool Sweep(Vector3 position, Vector3 direction, float distance, out float allowed, out Vector3 normal)
        {
            allowed = distance;
            normal = Vector3.zero;
            if (distance <= .000001f) return false;
            Vector3 low, high;
            Capsule(position, out low, out high);
            int count = physicsScene.CapsuleCast(low, high, BodyRadius, direction, hits,
                distance + Clearance, WorldMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) { allowed = 0; normal = -direction; return true; }
            bool hit = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = hits[i];
                if (!candidate.collider || IsOwn(candidate.collider)) continue;
                // Unity's nonalloc results are unordered; select the closest ourselves.
                float safe = Mathf.Max(0f, candidate.distance - Clearance);
                if (safe <= allowed)
                {
                    allowed = safe;
                    normal = candidate.normal;
                    hit = true;
                }
            }
            return hit;
        }

        private Vector3 ChooseSteering(Vector3 desired, float horizon, out float free)
        {
            Vector3 best = desired;
            float bestScore = float.NegativeInfinity;
            free = 0f;
            for (int i = 0; i < SteeringAngles.Length; i++)
            {
                float angle = SteeringAngles[i];
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * desired;
                Vector3 heading = velocity.sqrMagnitude > .0025f ? velocity.normalized : candidate;
                int count;
                float travel = TraceChain(transform.position, heading, candidate, horizon, scratch, out count);
                float alignment = Vector3.Dot(candidate, desired);
                // Reward useful travel, not just pointing into a wall. A small persistent
                // side preference avoids alternating left/right on symmetric obstacles.
                float fraction = travel / horizon;
                float score = fraction * (1.5f + alignment) + alignment * .25f;
                score += Vector3.Dot(candidate, steering) * .08f;
                if (preferredSide != 0f && Mathf.Sign(angle) == preferredSide && angle != 0f) score += .08f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    free = travel;
                }
            }
            float cross = Vector3.Cross(desired, best).y;
            if (Mathf.Abs(cross) > .1f) preferredSide = Mathf.Sign(cross);
            return best;
        }

        // Each sample sweeps the *whole physical capsule*, not a point or sphere ray.
        // Integrating a limited heading change produces an actual curved chain.
        private float TraceChain(Vector3 origin, Vector3 heading, Vector3 desired, float length,
            Vector3[] points, out int count)
        {
            points[0] = origin;
            count = 1;
            float travelled = 0f;
            float segment = length / ChainSegments;
            for (int i = 0; i < ChainSegments; i++)
            {
                Vector3 nextHeading = Vector3.RotateTowards(heading, desired, .28f, 0f).normalized;
                Vector3 direction = (heading + nextHeading).normalized;
                if (direction.sqrMagnitude < .5f) direction = nextHeading;
                float allowed;
                Vector3 normal;
                bool hit = Sweep(origin, direction, segment, out allowed, out normal);
                origin += direction * allowed;
                points[count++] = origin;
                travelled += allowed;
                if (hit) break;
                heading = nextHeading;
            }
            return travelled;
        }

        private void SweepAndSlide(Vector3 displacement)
        {
            Vector3 remainder = displacement;
            float budget = Mathf.Min(MaximumDisplacement, displacement.magnitude);
            // Three bounded contact iterations handle a face and an adjacent corner.
            for (int iteration = 0; iteration < 3 && budget > .00001f; iteration++)
            {
                float length = Mathf.Min(remainder.magnitude, budget);
                if (length <= .00001f) break;
                Vector3 direction = remainder.normalized;
                float allowed;
                Vector3 normal;
                bool hit = Sweep(transform.position, direction, length, out allowed, out normal);
                Vector3 before = transform.position;
                if (allowed > .00001f) controller.Move(direction * allowed);
                float moved = Flat(transform.position - before).magnitude;
                budget = Mathf.Max(0f, budget - moved);
                if (!hit) break;
                normal = Flat(normal);
                if (normal.sqrMagnitude < .01f) break;
                normal.Normalize();
                remainder = Vector3.ProjectOnPlane(direction * Mathf.Max(0f, length - allowed), normal);
                remainder.y = 0f;
            }
        }

        private float UpdatePredictions(float length = .65f)
        {
            if (!initialized || !physicsScene.IsValid()) return 0f;
            Vector3 facing = Flat(transform.forward).normalized;
            Vector3 course = HasTarget && steering.sqrMagnitude > .5f ? steering : facing;
            Vector3 heading = velocity.sqrMagnitude > .0025f ? velocity.normalized : facing;
            // Label by the body front/rear while retaining the velocity bend during turns.
            bool reverse = Vector3.Dot(heading, facing) < 0f;
            Vector3 frontHeading = reverse ? -heading : heading;
            Vector3 frontCourse = Vector3.Dot(course, facing) < 0f ? -course : course;
            float frontFree = TraceChain(transform.position, frontHeading, frontCourse, length, front, out frontCount);
            float rearFree = TraceChain(transform.position, -frontHeading, -frontCourse, length, rear, out rearCount);
            RenderPredictions();
            return Vector3.Dot(course, facing) < 0f ? rearFree : frontFree;
        }

        private void RenderPredictions()
        {
            if (!showPrediction) return;
            EnsurePredictionVisuals();
            if (!predictionRoot) return;
            predictionRoot.gameObject.SetActive(true);
            DrawChain(frontLine, frontDots, front, frontCount);
            DrawChain(rearLine, rearDots, rear, rearCount);
        }

        private void EnsurePredictionVisuals()
        {
            if (predictionRoot && frontLine && rearLine) return;
            if (predictionRoot) DestroyOwned(predictionRoot.gameObject);
            if (!predictionMaterial)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (!shader) shader = Shader.Find("Unlit/Color");
                if (!shader) return;
                predictionMaterial = new Material(shader) { name = "ECHO prediction (owned)" };
                ownsPredictionMaterial = true;
            }
            predictionRoot = new GameObject("Computed capsule predictions").transform;
            predictionRoot.SetParent(transform, false);
            CreateChainVisual("Front sweep", new Color(.18f, .95f, 1f, .8f), out frontLine, out frontDots);
            CreateChainVisual("Rear sweep", new Color(1f, .65f, .27f, .7f), out rearLine, out rearDots);
        }

        private void CreateChainVisual(string name, Color color, out LineRenderer line, out Transform[] dots)
        {
            var group = new GameObject(name);
            group.transform.SetParent(predictionRoot, false);
            line = group.AddComponent<LineRenderer>();
            line.sharedMaterial = predictionMaterial;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = .017f;
            line.startColor = line.endColor = color;
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            dots = new Transform[ChainSegments + 1];
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            for (int i = 0; i < dots.Length; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "Sweep sample " + i;
                dot.transform.SetParent(group.transform, false);
                dot.transform.localScale = Vector3.one * .055f;
                Collider collider = dot.GetComponent<Collider>();
                collider.enabled = false;
                DestroyOwned(collider);
                Renderer renderer = dot.GetComponent<Renderer>();
                renderer.sharedMaterial = predictionMaterial;
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                dots[i] = dot.transform;
            }
        }

        private static void DrawChain(LineRenderer line, Transform[] dots, Vector3[] samples, int count)
        {
            line.enabled = true;
            line.positionCount = count;
            // The height offset exposes the sampled capsule centres above the ground.
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                Vector3 centre = samples[i] + Vector3.up * (BodyHeight * .5f);
                line.SetPosition(i, centre);
                dots[i].position = centre;
            }
        }

        private void OnDestroy()
        {
            if (predictionRoot) DestroyOwned(predictionRoot.gameObject);
            if (predictionMaterial && ownsPredictionMaterial)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(predictionMaterial)) return;
#endif
                DestroyOwned(predictionMaterial);
            }
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        private static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
    }
}
