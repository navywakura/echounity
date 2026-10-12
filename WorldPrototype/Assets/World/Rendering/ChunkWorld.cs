using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EchoWorld
{
    /// <summary>
    /// Bounded column streaming for the classic render pipeline. Global voxel/path coordinates
    /// belong to WorldData; only the view and FollowTarget use positions relative to Origin.
    /// </summary>
    public sealed class ChunkWorld : MonoBehaviour
    {
        public WorldData Data { get; private set; }
        public Vector2Int Origin { get; set; }
        public Transform FollowTarget;

        [SerializeField, Range(1, 4)] private int radius = 2;
        public int Radius { get { return radius; } }
        public int LoadedCount { get { return columns.Count; } }
        public int PendingCount { get { return pending.Count; } }
        public int VertexCount { get; private set; }
        public float LastBuildMs { get; private set; }
        public Material BuiltMaterial { get { return GetMaterial(); } }

        private readonly Dictionary<Vector2Int, ColumnView> columns = new Dictionary<Vector2Int, ColumnView>();
        private readonly HashSet<Vector2Int> needed = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> resident = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> pending = new HashSet<Vector2Int>();
        private readonly Queue<Vector2Int> buildQueue = new Queue<Vector2Int>();
        private Vector2Int center;
        private Vector2Int placedOrigin;
        private int streamedRadius;
        private bool hasCenter;
        private Material material;
        private Material treeMaterial;
        private bool triedTreeMaterial;

        /// <summary>Reset ownership, set Data, and queue the region. FollowTarget may be set before or after this.</summary>
        public void Initialize(WorldData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ReleaseColumnsAndData();
            Data = data;
            Origin = Vector2Int.zero;
            placedOrigin = Origin;
            LastBuildMs = 0;
            triedTreeMaterial = false;
            radius = Mathf.Clamp(radius, 1, 4);
            Refresh();
        }

        private void Update()
        {
            TickStreaming();
        }

        /// <summary>Advance streaming by one column; callable directly by Edit Mode verification.</summary>
        public void TickStreaming()
        {
            if (Data == null) return;
            RebaseIfNeeded();
            Refresh();
            BuildNextColumn();
        }

        /// <summary>
        /// Reposition after public Origin changes, then update residency only on a chunk/radius change.
        /// The extra data-only column around the active square supplies persisted seam edits.
        /// </summary>
        public void Refresh()
        {
            RepositionColumns();
            if (Data == null) return;
            radius = Mathf.Clamp(radius, 1, 4);
            Vector3Int global = FollowTarget != null ? ToGlobal(FollowTarget.position) : new Vector3Int(Origin.x, 0, Origin.y);
            int x = Mathf.Clamp(global.x, WorldData.WorldMin, WorldData.WorldMax - 1);
            int z = Mathf.Clamp(global.z, WorldData.WorldMin, WorldData.WorldMax - 1);
            Vector2Int nextCenter = WorldData.Column(x, z);
            if (hasCenter && center == nextCenter && streamedRadius == radius) return;

            center = nextCenter;
            hasCenter = false;
            needed.Clear();
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector2Int column = center + new Vector2Int(dx, dz);
                    if (VoxelMesher.IsValidColumn(column)) needed.Add(column);
                }

            // Cancel the old generation's queued work before loading or building anything new.
            buildQueue.Clear();
            pending.Clear();
            var evicted = new List<Vector2Int>();
            foreach (Vector2Int column in columns.Keys)
                if (!needed.Contains(column)) evicted.Add(column);
            foreach (Vector2Int column in evicted)
            {
                ColumnView view = columns[column];
                VertexCount -= view.VertexCount;
                DestroyColumn(view);
                columns.Remove(column);
            }

            var nextResident = new HashSet<Vector2Int>();
            int halo = radius + 1;
            for (int dz = -halo; dz <= halo; dz++)
                for (int dx = -halo; dx <= halo; dx++)
                {
                    Vector2Int column = center + new Vector2Int(dx, dz);
                    if (VoxelMesher.IsValidColumn(column)) nextResident.Add(column);
                }
            evicted.Clear();
            foreach (Vector2Int column in resident)
                if (!nextResident.Contains(column)) evicted.Add(column);
            foreach (Vector2Int column in evicted)
            {
                Data.UnloadColumn(column); // WorldData persists edits before evicting its cache.
                resident.Remove(column);
            }
            foreach (Vector2Int column in nextResident)
                if (!resident.Contains(column))
                {
                    Data.LoadColumn(column);
                    resident.Add(column);
                }

            var ordered = new List<Vector2Int>(needed);
            ordered.Sort(CompareDistanceToCenter);
            foreach (Vector2Int column in ordered)
                if (!columns.TryGetValue(column, out ColumnView view) || view.Dirty) Enqueue(column);
            streamedRadius = radius;
            hasCenter = true;
        }

        /// <summary>
        /// Explicit verification/bootstrap only: finish at most the central radius-two square (25
        /// columns). At radius three/four, outer columns remain on the one-column-per-Update queue.
        /// </summary>
        public void BuildImmediate()
        {
            Refresh();
            int count = buildQueue.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2Int column = buildQueue.Dequeue();
                if (!pending.Contains(column)) continue;
                if (!needed.Contains(column)) { pending.Remove(column); continue; }
                if (Mathf.Abs(column.x - center.x) > 2 || Mathf.Abs(column.y - center.y) > 2)
                {
                    buildQueue.Enqueue(column);
                    continue;
                }
                BuildQueuedColumn(column);
            }
        }

        public Vector3 ToLocal(Vector3Int global)
        {
            return new Vector3(global.x - Origin.x, global.y, global.z - Origin.y);
        }

        public Vector3Int ToGlobal(Vector3 local)
        {
            // Never add a multi-million-unit origin in float space: it would discard sub-voxel precision.
            return new Vector3Int(Origin.x + Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y),
                Origin.y + Mathf.FloorToInt(local.z));
        }

        public void NotifyEdit(Vector3Int global)
        {
            if (Data == null || !Data.InBounds(global.x, global.y, global.z)) return;
            Vector2Int column = WorldData.Column(global.x, global.z);
            MarkDirty(column);
            int localX = global.x - column.x * WorldData.ChunkSize;
            int localZ = global.z - column.y * WorldData.ChunkSize;
            if (localX == 0) MarkDirty(column + Vector2Int.left);
            if (localX == WorldData.ChunkSize - 1) MarkDirty(column + Vector2Int.right);
            if (localZ == 0) MarkDirty(column + Vector2Int.down);
            if (localZ == WorldData.ChunkSize - 1) MarkDirty(column + Vector2Int.up);
        }

        public bool IsColumnReady(int x, int z)
        {
            Vector2Int column = WorldData.Column(x, z);
            return Data != null && Data.InBounds(x, WorldData.MinY, z) && needed.Contains(column) &&
                !pending.Contains(column) && columns.TryGetValue(column, out ColumnView view) && !view.Dirty &&
                view.Root != null && view.Root.activeInHierarchy && view.Collider != null &&
                view.Collider.enabled && view.Collider.sharedMesh != null && view.Collider.sharedMesh == view.CollisionMesh;
        }

        /// <summary>Shift only the target root and column views. Cameras follow the target; paths stay global.</summary>
        public void RebaseIfNeeded()
        {
            if (FollowTarget == null) return;
            Vector3 local = FollowTarget.position;
            if (Mathf.Abs(local.x) <= 128 && Mathf.Abs(local.z) <= 128) return;
            if (float.IsNaN(local.x) || float.IsNaN(local.z) || float.IsInfinity(local.x) || float.IsInfinity(local.z)) return;
            Vector3Int global = ToGlobal(local);
            var nextOrigin = new Vector2Int(
                WorldData.FloorDiv(Mathf.Clamp(global.x, WorldData.WorldMin, WorldData.WorldMax - 1), WorldData.ChunkSize) * WorldData.ChunkSize,
                WorldData.FloorDiv(Mathf.Clamp(global.z, WorldData.WorldMin, WorldData.WorldMax - 1), WorldData.ChunkSize) * WorldData.ChunkSize);
            Vector2Int offset = nextOrigin - Origin;
            if (offset == Vector2Int.zero) return;
            CharacterController controller = FollowTarget.GetComponent<CharacterController>();
            bool restoreController = controller != null && controller.enabled;
            if (restoreController) controller.enabled = false;
            try
            {
                Origin = nextOrigin;
                FollowTarget.position = local - new Vector3(offset.x, 0, offset.y);
                RepositionColumns();
            }
            finally
            {
                if (restoreController && controller != null) controller.enabled = true;
            }
            Refresh();
            Physics.SyncTransforms();
        }

        public void SetRadius(int value)
        {
            radius = Mathf.Clamp(value, 1, 4);
            Refresh();
        }

        /// <summary>
        /// Testable mesh factory. Vertices are relative to the column's integer minimum X/Z, with
        /// absolute world Y. Loads the column's 3x3 data neighborhood; the caller owns data residency
        /// and the returned Mesh. ChunkWorld keeps those data columns inside its bounded halo.
        /// </summary>
        public static Mesh BuildMesh(WorldData data, Vector2Int column)
        {
            return VoxelMesher.Build(data, column);
        }

        private void MarkDirty(Vector2Int column)
        {
            if (!needed.Contains(column)) return;
            if (columns.TryGetValue(column, out ColumnView view)) view.Dirty = true;
            Enqueue(column);
        }

        private void Enqueue(Vector2Int column)
        {
            if (pending.Add(column)) buildQueue.Enqueue(column);
        }

        private void BuildNextColumn()
        {
            while (buildQueue.Count > 0)
            {
                Vector2Int column = buildQueue.Dequeue();
                if (!pending.Contains(column)) continue;
                if (!needed.Contains(column)) { pending.Remove(column); continue; }
                BuildQueuedColumn(column);
                return;
            }
        }

        private void BuildQueuedColumn(Vector2Int column)
        {
            try
            {
                BuildColumn(column);
                pending.Remove(column);
            }
            catch
            {
                // A failed mesh/collider must never become "ready" or silently fall off the queue.
                buildQueue.Enqueue(column);
                throw;
            }
        }

        private void BuildColumn(Vector2Int column)
        {
            Stopwatch timer = Stopwatch.StartNew();
            Mesh replacement = null;
            Mesh replacementCollision = null;
            Mesh replacementTrees = null;
            ColumnView view = null;
            bool created = false;
            try
            {
                Material sharedMaterial = GetMaterial();
                Material sharedTrees = GetTreeMaterial();
                TreeSprites.Batch trees = sharedTrees != null ? TreeSprites.Collect(Data, column) : null;
                replacement = VoxelMesher.Build(Data, column, trees != null ? trees.HiddenVoxels : null, out replacementCollision);
                if (trees != null) replacementTrees = TreeSprites.Build(Data, column, trees);
                if (!columns.TryGetValue(column, out view))
                {
                    view = new ColumnView(column, transform);
                    created = true;
                }
                PositionColumn(column, view);
                Mesh previous = view.Mesh;
                Mesh previousCollision = view.CollisionMesh;
                Mesh previousTrees = view.TreeMesh;
                int previousVertices = view.VertexCount;
                if (replacementTrees != null) view.EnsureTreeView();
                view.Collider.sharedMesh = null;
                if (replacementCollision.vertexCount > 0) view.Collider.sharedMesh = replacementCollision;
                view.Filter.sharedMesh = replacement;
                view.Renderer.sharedMaterial = sharedMaterial;
                if (view.TreeFilter != null)
                {
                    view.TreeFilter.sharedMesh = replacementTrees;
                    view.TreeRenderer.sharedMaterial = sharedTrees;
                    view.TreeFilter.gameObject.SetActive(replacementTrees != null);
                }
                view.Mesh = replacement;
                view.CollisionMesh = replacementCollision;
                view.TreeMesh = replacementTrees;
                view.Dirty = false;
                if (created) columns.Add(column, view);
                VertexCount += view.VertexCount - previousVertices;
                replacement = null; // The column now owns it.
                replacementCollision = null;
                replacementTrees = null;
                if (previousCollision != previous) DestroyOwned(previousCollision);
                DestroyOwned(previous);
                DestroyOwned(previousTrees);
            }
            catch
            {
                if (created && view != null) DestroyColumn(view);
                else if (view != null)
                {
                    view.Collider.sharedMesh = view.CollisionMesh;
                    view.Filter.sharedMesh = view.Mesh;
                    if (view.TreeFilter != null)
                    {
                        view.TreeFilter.sharedMesh = view.TreeMesh;
                        view.TreeFilter.gameObject.SetActive(view.TreeMesh != null);
                    }
                }
                if (replacementCollision != replacement) DestroyOwned(replacementCollision);
                DestroyOwned(replacement);
                DestroyOwned(replacementTrees);
                throw;
            }
            finally
            {
                timer.Stop();
                LastBuildMs = (float)timer.Elapsed.TotalMilliseconds;
            }
        }

        private void RepositionColumns()
        {
            if (placedOrigin == Origin) return;
            foreach (KeyValuePair<Vector2Int, ColumnView> entry in columns) PositionColumn(entry.Key, entry.Value);
            placedOrigin = Origin;
        }

        private void PositionColumn(Vector2Int column, ColumnView view)
        {
            view.Root.transform.position = ToLocal(new Vector3Int(column.x * WorldData.ChunkSize, 0, column.y * WorldData.ChunkSize));
            // Modulo in integer space preserves texture phase even near the 5-million-unit edge.
            int x = column.x * WorldData.ChunkSize, z = column.y * WorldData.ChunkSize;
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_EchoTextureOffset", new Vector4((x % 1024 + 1024) % 1024, 0, (z % 1024 + 1024) % 1024, 0));
            view.Renderer.SetPropertyBlock(properties);
        }

        private int CompareDistanceToCenter(Vector2Int a, Vector2Int b)
        {
            Vector2Int da = a - center;
            Vector2Int db = b - center;
            int distance = da.sqrMagnitude.CompareTo(db.sqrMagnitude);
            if (distance != 0) return distance;
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.y.CompareTo(b.y);
        }

        private Material GetMaterial()
        {
            if (material != null) return material;
            Shader shader = Shader.Find("EchoWorld/VoxelTerrain");
            if (shader == null) throw new InvalidOperationException("Missing shader EchoWorld/VoxelTerrain. Import Assets/World/Shaders first.");
            material = new Material(shader) { name = "ECHO terrain (shared triplanar textures)" };
            material.SetVector("_TerrainTextures", new Vector4(
                LoadTexture(material, "_GrassTex", "grass"), LoadTexture(material, "_RockTex", "rock"),
                LoadTexture(material, "_SnowTex", "snow"), LoadTexture(material, "_BarkTex", "bark")));
            material.SetVector("_DetailTextures", new Vector4(
                LoadTexture(material, "_MetalTex", "metal"), LoadTexture(material, "_CrateTex", "crate"), 0, 0));
            return material;
        }

        private static float LoadTexture(Material target, string property, string resource)
        {
            Texture2D texture = Resources.Load<Texture2D>("Art/Terrain/" + resource);
            if (texture == null) return 0;
            target.SetTexture(property, texture);
            return 1;
        }

        private Material GetTreeMaterial()
        {
            if (treeMaterial != null || triedTreeMaterial) return treeMaterial;
            triedTreeMaterial = true;
            Texture2D atlas = Resources.Load<Texture2D>("Art/Tiles/seasons_trees");
            Shader shader = Shader.Find("EchoWorld/TreeBillboard");
            if (atlas == null || shader == null || !shader.isSupported) return null;
            treeMaterial = new Material(shader) { name = "ECHO trees (original seasonal atlas)", mainTexture = atlas };
            return treeMaterial;
        }

        private static void DestroyColumn(ColumnView view)
        {
            if (view.Root != null)
            {
                view.Root.SetActive(false);
                view.Filter.sharedMesh = null;
                view.Collider.sharedMesh = null;
                if (view.TreeFilter != null) view.TreeFilter.sharedMesh = null;
                view.Root.transform.SetParent(null, false);
                DestroyOwned(view.Root);
            }
            DestroyOwned(view.Mesh);
            if (view.CollisionMesh != view.Mesh) DestroyOwned(view.CollisionMesh);
            DestroyOwned(view.TreeMesh);
        }

        private void ReleaseColumnsAndData()
        {
            foreach (ColumnView view in columns.Values) DestroyColumn(view);
            columns.Clear();
            buildQueue.Clear();
            pending.Clear();
            needed.Clear();
            VertexCount = 0;
            hasCenter = false;
            if (Data != null)
            {
                // Remove only after a successful unload, so an I/O failure can be retried.
                foreach (Vector2Int column in new List<Vector2Int>(resident))
                {
                    Data.UnloadColumn(column);
                    resident.Remove(column);
                }
            }
            resident.Clear();
        }

        private void OnDestroy()
        {
            try { ReleaseColumnsAndData(); }
            finally
            {
                DestroyOwned(material); material = null;
                DestroyOwned(treeMaterial); treeMaterial = null;
            }
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }

        private sealed class ColumnView
        {
            public readonly GameObject Root;
            public readonly MeshFilter Filter;
            public readonly MeshRenderer Renderer;
            public readonly MeshCollider Collider;
            public Mesh Mesh;
            public Mesh CollisionMesh;
            public Mesh TreeMesh;
            public MeshFilter TreeFilter;
            public MeshRenderer TreeRenderer;
            public int VertexCount { get { return (Mesh != null ? Mesh.vertexCount : 0) + (TreeMesh != null ? TreeMesh.vertexCount : 0); } }
            public bool Dirty = true;

            public ColumnView(Vector2Int column, Transform parent)
            {
                Root = new GameObject("Column " + column.x + "," + column.y,
                    typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                Root.layer = 0;
                Root.transform.SetParent(parent, false);
                Filter = Root.GetComponent<MeshFilter>();
                Renderer = Root.GetComponent<MeshRenderer>();
                Collider = Root.GetComponent<MeshCollider>();
            }

            public void EnsureTreeView()
            {
                if (TreeFilter != null) return;
                var child = new GameObject("Tree sprites", typeof(MeshFilter), typeof(MeshRenderer));
                child.layer = 2; // Visual only. Raycasts and physics use the column's voxel collider.
                child.transform.SetParent(Root.transform, false);
                TreeFilter = child.GetComponent<MeshFilter>();
                TreeRenderer = child.GetComponent<MeshRenderer>();
            }
        }
    }
}
