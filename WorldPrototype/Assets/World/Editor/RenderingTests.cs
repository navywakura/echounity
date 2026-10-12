using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EchoWorld
{
    /// <summary>Real WorldData/mesh/collider checks; run in the Unity Editor, without NUnit.</summary>
    public static class RenderingTests
    {
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, Vector3Int.forward, Vector3Int.back
        };

        public static List<string> RunAll()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Run RenderingTests in Edit Mode before entering Play.");

            var passed = new List<string>();
            Run(passed, nameof(SurfaceFacesMatchBlockAuthority), SurfaceFacesMatchBlockAuthority);
            Run(passed, nameof(AdjacentBlocksHideSharedFaces), AdjacentBlocksHideSharedFaces);
            Run(passed, nameof(TorchesDoNotOccludeOrCollide), TorchesDoNotOccludeOrCollide);
            Run(passed, nameof(MaterialIdsReachEveryFace), MaterialIdsReachEveryFace);
            Run(passed, nameof(MaterialTexturesAndNightFloor), MaterialTexturesAndNightFloor);
            Run(passed, nameof(TreeBatchesRespectDamageAndFallback), TreeBatchesRespectDamageAndFallback);
            Run(passed, nameof(GeneratedCavesAndStructuresAreVisible), GeneratedCavesAndStructuresAreVisible);
            Run(passed, nameof(DeepExcavationsAndBoundaryEdits), DeepExcavationsAndBoundaryEdits);
            Run(passed, nameof(BoundaryEditDirtiesBothColumns), BoundaryEditDirtiesBothColumns);
            Run(passed, nameof(DirtyMeshReplacesCollider), DirtyMeshReplacesCollider);
            Run(passed, nameof(LargeOriginRoundTripAndRebase), LargeOriginRoundTripAndRebase);
            Run(passed, nameof(RefreshRepositionsExistingChunks), RefreshRepositionsExistingChunks);
            Run(passed, nameof(StreamingBuildsOneColumnPerUpdate), StreamingBuildsOneColumnPerUpdate);
            Run(passed, nameof(ResidencyAndQueueStayBoundedAfterTeleports), ResidencyAndQueueStayBoundedAfterTeleports);
            Run(passed, nameof(LargeEditedMeshUses32BitIndices), LargeEditedMeshUses32BitIndices);
            Run(passed, nameof(WorldEdgeFacesStayInBounds), WorldEdgeFacesStayInBounds);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new InvalidOperationException("RenderingTests." + name + " failed", error); }
        }

        // A surface-only mesher that misses cliffs, bottoms or uses reversed winding fails this oracle.
        private static void SurfaceFacesMatchBlockAuthority()
        {
            using (var fixture = new Fixture())
            {
                Vector2Int column = new Vector2Int(-1, 0);
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, column);
                try
                {
                    AssertMatchesBlocks(fixture.Data, column, mesh);
                    Check(mesh.vertexCount >= 16 * 16 * 4, "Expected a visible terrain surface.");
                    Check(mesh.vertexCount < 16 * 16 * 400, "Unedited terrain must remain a small surface mesh.");
                    Check(mesh.colors32.Length == mesh.vertexCount, "Every vertex needs its procedural color.");
                    var colors = new HashSet<Color32>(mesh.colors32);
                    Check(colors.Count > 2, "Terrain should have visible surface/material variation.");
                    Check(mesh.bounds.min.x >= 0 && mesh.bounds.max.x <= 16 &&
                          mesh.bounds.min.z >= 0 && mesh.bounds.max.z <= 16,
                          "Mesh vertices must be column-local integer cube corners.");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        // Two isolated neighboring cubes have ten exposed quads, not twelve.
        private static void AdjacentBlocksHideSharedFaces()
        {
            using (var fixture = new Fixture())
            {
                Vector2Int column = Vector2Int.zero;
                int baseline = CountVertices(fixture.Data, column);
                var first = new Vector3Int(6, 150, 6);
                var second = new Vector3Int(7, 150, 6);
                Check(fixture.Data.GetBlock(6, 150, 6) == 0 && fixture.Data.GetBlock(7, 150, 6) == 0,
                    "Fixture cubes must start in air.");
                Set(fixture.Data, first, 3);
                Check(CountVertices(fixture.Data, column) == baseline + 24, "An isolated cube has six faces.");
                Set(fixture.Data, second, 4);
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, column);
                try
                {
                    Check(mesh.vertexCount == baseline + 40, "Adjacent solids must suppress both shared faces.");
                    HashSet<Face> faces = ReadFaces(column, mesh);
                    Check(!faces.Contains(new Face(first, Vector3Int.right)), "First shared face survived.");
                    Check(!faces.Contains(new Face(second, Vector3Int.left)), "Second shared face survived.");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        private static void TorchesDoNotOccludeOrCollide()
        {
            using (var fixture = new Fixture())
            {
                int baseline = CountVertices(fixture.Data, Vector2Int.zero);
                var solid = new Vector3Int(6, 150, 6);
                var torch = solid + Vector3Int.right;
                Set(fixture.Data, solid, 3);
                Set(fixture.Data, torch, 5);
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, Vector2Int.zero);
                try
                {
                    Check(mesh.vertexCount == baseline + 24, "Torch visuals/lights belong to gameplay, with no voxel collider.");
                    Check(ReadFaces(Vector2Int.zero, mesh).Contains(new Face(solid, Vector3Int.right)),
                        "A torch must not occlude the solid face beside it.");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        private static void MaterialIdsReachEveryFace()
        {
            using (var fixture = new Fixture())
            {
                for (byte id = 1; id <= 10; id++)
                    if (id != 5) Set(fixture.Data, new Vector3Int(id, 150 + id * 2, 7), id);
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, Vector2Int.zero);
                try
                {
                    Vector2[] materials = mesh.uv;
                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    Check(materials.Length == vertices.Length, "Every face requires a material ID for triplanar sampling.");
                    for (int i = 0; i < vertices.Length; i += 4)
                    {
                        Vector3 center = (vertices[i] + vertices[i + 1] + vertices[i + 2] + vertices[i + 3]) * 0.25f;
                        Vector3Int cell = Vector3Int.FloorToInt(center - normals[i] * 0.5f);
                        byte id = fixture.Data.GetBlock(cell.x, cell.y, cell.z);
                        for (int j = 0; j < 4; j++)
                            Check(materials[i + j].x == id, "A face sampled another block's material.");
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        private static void MaterialTexturesAndNightFloor()
        {
            using (var fixture = new Fixture(true))
            {
                Material material = fixture.World.BuiltMaterial;
                string[] names = { "grass", "rock", "snow", "bark", "metal", "crate" };
                string[] properties = { "_GrassTex", "_RockTex", "_SnowTex", "_BarkTex", "_MetalTex", "_CrateTex" };
                Vector4 terrain = material.GetVector("_TerrainTextures"), detail = material.GetVector("_DetailTextures");
                for (int i = 0; i < names.Length; i++)
                {
                    Texture2D resource = Resources.Load<Texture2D>("Art/Terrain/" + names[i]);
                    float available = i < 4 ? terrain[i] : detail[i - 4];
                    Check(available == (resource != null ? 1 : 0), "Missing textures must enable vertex-color fallback.");
                    if (resource != null) Check(material.GetTexture(properties[i]) == resource, "Texture binding failed for " + names[i]);
                }
                Check(material.GetFloat("_Ambient") <= 0.025f, "Ambient floor must allow torch-lit nights.");
            }
        }

        private static void TreeBatchesRespectDamageAndFallback()
        {
            using (var fixture = new Fixture(true))
            {
                // Fixed forest column for seed 72831; no unbounded search or full-volume scan.
                var column = new Vector2Int(-3, -3);
                fixture.World.Origin = new Vector2Int(-48, -48);
                fixture.World.Refresh();
                fixture.World.BuildImmediate();
                MeshFilter terrain = FindColumn(fixture.World, column);
                MeshCollider collider = terrain.GetComponent<MeshCollider>();
                Mesh authority = ChunkWorld.BuildMesh(fixture.Data, column);
                try
                {
                    Check(ReadFaces(column, collider.sharedMesh).SetEquals(ReadFaces(column, authority)),
                        "Sprites must preserve the complete authoritative voxel collider.");
                }
                finally { Object.DestroyImmediate(authority); }
                Transform batch = terrain.transform.Find("Tree sprites");
                Shader shader = Shader.Find("EchoWorld/TreeBillboard");
                if (Resources.Load<Texture2D>("Art/Tiles/seasons_trees") == null || shader == null || !shader.isSupported)
                {
                    Check(batch == null && collider.sharedMesh == terrain.sharedMesh, "Missing sprite resources must retain voxel trees.");
                    return;
                }
                Check(batch != null && batch.gameObject.activeSelf, "The forest must use a single tree sprite batch.");
                MeshFilter trees = batch.GetComponent<MeshFilter>();
                int vertices = trees.sharedMesh.vertexCount;
                Check(vertices > 0 && vertices % 4 == 0, "Every tree must occupy one billboard quad.");
                Vector3 firstAnchor = trees.sharedMesh.vertices[0];
                var root = new Vector3Int(column.x * 16 + Mathf.FloorToInt(firstAnchor.x), Mathf.RoundToInt(firstAnchor.y),
                    column.y * 16 + Mathf.FloorToInt(firstAnchor.z));
                Check(fixture.Data.GetBlock(root.x, root.y, root.z) == 6, "Sprite anchor must coincide with its voxel trunk.");
                Check(!ReadFaces(column, terrain.sharedMesh).Contains(new Face(root, Vector3Int.left)),
                    "An intact sprite must suppress its duplicate voxel tree.");
                foreach (Vector3 anchor in trees.sharedMesh.vertices)
                    Check(trees.sharedMesh.bounds.Contains(anchor), "Billboard bounds must contain every tree anchor.");
                var damage = root + Vector3Int.up;
                Set(fixture.Data, damage, 0);
                fixture.World.NotifyEdit(damage);
                fixture.World.BuildImmediate();
                int after = trees.sharedMesh != null ? trees.sharedMesh.vertexCount : 0;
                Check(after <= vertices - 4, "A damaged trunk must immediately lose its intact-tree sprite.");
                Check(ReadFaces(column, terrain.sharedMesh).Contains(new Face(root, Vector3Int.up)),
                    "Damage must reveal authoritative remaining voxels, including the cut trunk face.");
                Set(fixture.Data, root, 0);
                fixture.World.NotifyEdit(root);
                fixture.World.BuildImmediate();
                Check((trees.sharedMesh != null ? trees.sharedMesh.vertexCount : 0) <= after,
                    "Removing the base must never resurrect its tree sprite.");
                AssertMetrics(fixture.World);
            }
        }

        private static void GeneratedCavesAndStructuresAreVisible()
        {
            using (var fixture = new Fixture())
            {
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, Vector2Int.zero);
                try
                {
                    HashSet<Face> faces = ReadFaces(Vector2Int.zero, mesh);
                    Check(faces.Contains(new Face(new Vector3Int(8, 4, 7), Vector3Int.up)), "Generated house roof is missing.");
                    Check(faces.Contains(new Face(new Vector3Int(9, 1, 7), Vector3Int.up)), "Generated chest is missing.");
                    bool cave = false;
                    foreach (Face face in faces)
                        if (face.Cell.y > -64 && face.Cell.y < -5 && face.Normal != Vector3Int.down) { cave = true; break; }
                    Check(cave, "Generator v2 must expose underground cave faces without excavation edits.");
                    AssertMatchesBlocks(fixture.Data, Vector2Int.zero, mesh);
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        // Edits far below the generated height must discover surrounding solid cells, across a seam too.
        private static void DeepExcavationsAndBoundaryEdits()
        {
            using (var fixture = new Fixture())
            {
                for (int x = 13; x <= 19; x++) Set(fixture.Data, new Vector3Int(x, -170, 8), 0);
                for (int y = -175; y <= fixture.Data.SurfaceHeight(8, 8) + 2; y++)
                    Set(fixture.Data, new Vector3Int(8, y, 8), 0, false);
                Set(fixture.Data, new Vector3Int(15, 150, 8), 5);
                Set(fixture.Data, new Vector3Int(16, 150, 8), 4);

                // Both sides must reload their persisted deltas before candidate discovery/occlusion.
                fixture.Data.SaveAll();
                fixture.Data.UnloadColumn(Vector2Int.zero);
                fixture.Data.UnloadColumn(Vector2Int.right);

                foreach (Vector2Int column in new[] { Vector2Int.zero, Vector2Int.right })
                {
                    Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, column);
                    try { AssertMatchesBlocks(fixture.Data, column, mesh); }
                    finally { Object.DestroyImmediate(mesh); }
                }
            }
        }

        private static void BoundaryEditDirtiesBothColumns()
        {
            using (var fixture = new Fixture(true))
            {
                fixture.World.BuildImmediate();
                int vertices = fixture.World.VertexCount;
                var cell = new Vector3Int(15, -170, 8);
                Set(fixture.Data, cell, 0);
                fixture.World.NotifyEdit(cell);
                fixture.World.NotifyEdit(cell);
                Check(fixture.World.PendingCount == 2, "Boundary invalidation must deduplicate both columns.");
                Check(!fixture.World.IsColumnReady(15, 8) && !fixture.World.IsColumnReady(16, 8),
                    "Both sides of a dirty seam must be unready.");
                Check(fixture.World.IsColumnReady(-1, 8), "An unrelated column should remain ready.");
                fixture.World.BuildImmediate();
                Check(fixture.World.PendingCount == 0, "Bootstrap should drain a radius-one neighborhood.");
                Check(fixture.World.IsColumnReady(15, 8) && fixture.World.IsColumnReady(16, 8),
                    "Both rebuilt seam colliders should be ready.");
                Check(fixture.World.VertexCount == vertices + 24, "One underground hole must expose six faces.");

                Set(fixture.Data, new Vector3Int(-1, -170, -1), 0);
                fixture.World.NotifyEdit(new Vector3Int(-1, -170, -1));
                Check(fixture.World.PendingCount == 3, "Negative corner edits affect owner and two face neighbors.");
                Check(!fixture.World.IsColumnReady(-1, -1) && !fixture.World.IsColumnReady(0, -1) &&
                      !fixture.World.IsColumnReady(-1, 0), "Negative-boundary invalidation used truncating division.");
            }
        }

        private static void DirtyMeshReplacesCollider()
        {
            using (var fixture = new Fixture(true))
            {
                fixture.World.BuildImmediate();
                MeshFilter view = FindColumn(fixture.World, Vector2Int.zero);
                Mesh oldMesh = view.sharedMesh;
                int baseline = fixture.World.VertexCount;
                var cell = new Vector3Int(8, 150, 8);
                Set(fixture.Data, cell, 3);
                fixture.World.NotifyEdit(cell);
                Check(!fixture.World.IsColumnReady(8, 8), "Dirty collider must not report readiness.");
                fixture.World.BuildImmediate();
                Check(view.sharedMesh != oldMesh, "A dirty column must replace its mesh.");
                MeshCollider collider = view.GetComponent<MeshCollider>();
                Check(collider.sharedMesh == view.sharedMesh, "Collider and visible terrain must agree after an edit.");
                Check(fixture.World.VertexCount == baseline + 24, "VertexCount must reflect the replacement mesh.");
                Check(fixture.World.IsColumnReady(8, 8), "Rebuilt collider should be ready.");
                Physics.SyncTransforms();
                var ray = new Ray(fixture.World.ToLocal(cell) + new Vector3(0.5f, 3, 0.5f), Vector3.down);
                Check(collider.Raycast(ray, out RaycastHit hit, 4), "New block must be hit by the rebuilt collider.");
                Check(Mathf.Abs(hit.point.y - 151) < 0.001f && hit.normal.y > 0.99f,
                    "Collider hit must land on the outward top face of the integer-corner cube.");
                Check(fixture.World.ToGlobal(hit.point - hit.normal * 0.01f) == cell,
                    "Ray hit offset and ToGlobal floor must select the mined voxel.");

                Set(fixture.Data, cell, 0);
                fixture.World.NotifyEdit(cell);
                fixture.World.BuildImmediate();
                Check(fixture.World.VertexCount == baseline, "Removing the edit must restore baseline geometry.");
                AssertMetrics(fixture.World);
            }
        }

        private static void LargeOriginRoundTripAndRebase()
        {
            using (var fixture = new Fixture(true))
            {
                ChunkWorld world = fixture.World;
                world.Origin = new Vector2Int(4_900_000, -4_900_000);
                var global = new Vector3Int(4_900_007, -199, -4_900_003);
                Check(world.ToLocal(global) == new Vector3(7, -199, -3), "Subtract integer origin before using floats.");
                Check(world.ToGlobal(world.ToLocal(global)) == global, "Large-world integer round trip failed.");
                Check(world.ToGlobal(new Vector3(-0.25f, -0.25f, -0.25f)) ==
                      new Vector3Int(4_899_999, -1, -4_900_001), "Negative fractional positions must floor independently.");
                fixture.Player.transform.position = new Vector3(145.375f, 7.25f, -130.75f);
                CharacterController controller = fixture.Player.AddComponent<CharacterController>();
                Vector3Int before = world.ToGlobal(fixture.Player.transform.position);
                Vector3 destinationBefore = world.ToLocal(global);
                Vector2Int originBefore = world.Origin;
                world.RebaseIfNeeded();
                Vector2Int shift = world.Origin - originBefore;
                Check(shift.x % 16 == 0 && shift.y % 16 == 0 && shift != Vector2Int.zero,
                    "Origin changes must be nonzero multiples of a chunk.");
                Check(controller.enabled, "Rebase must restore an enabled CharacterController.");
                Check(fixture.Player.transform.position.y == 7.25f, "Rebase must preserve root foot height.");
                Check(world.ToGlobal(fixture.Player.transform.position) == before, "Rebase changed the player's global voxel.");
                Check(world.ToLocal(global) == destinationBefore - new Vector3(shift.x, 0, shift.y),
                    "A global path destination must translate correctly after rebasing.");
                Check(Mathf.Abs(fixture.Player.transform.position.x) <= 128 &&
                      Mathf.Abs(fixture.Player.transform.position.z) <= 128, "Player should return near the local origin.");
                controller.enabled = false;
                fixture.Player.transform.position += new Vector3(-160, 0, 0);
                world.RebaseIfNeeded();
                Check(!controller.enabled, "Rebasing must preserve an already disabled controller.");
            }
        }

        private static void RefreshRepositionsExistingChunks()
        {
            using (var fixture = new Fixture(true))
            {
                fixture.World.BuildImmediate();
                MeshFilter column = FindColumn(fixture.World, Vector2Int.zero);
                Mesh mesh = column.sharedMesh;
                var properties = new MaterialPropertyBlock();
                column.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Vector4 phase = properties.GetVector("_EchoTextureOffset");
                fixture.World.Origin = new Vector2Int(16, -32);
                fixture.Player.transform.position -= new Vector3(16, 0, -32);
                fixture.World.Refresh();
                Check(column.transform.position == new Vector3(-16, 0, 32),
                    "Refresh must honor a public Origin change even when the global center column stays the same.");
                Check(column.sharedMesh == mesh && fixture.World.PendingCount == 0,
                    "Changing only origin must reposition, without remeshing.");
                column.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Check(properties.GetVector("_EchoTextureOffset") == phase, "Rebasing must not shift triplanar texture phase.");
                AssertMetrics(fixture.World);
            }
        }

        private static void StreamingBuildsOneColumnPerUpdate()
        {
            using (var fixture = new Fixture(true, 2))
            {
                Check(fixture.World.LoadedCount == 0 && fixture.World.PendingCount == 25,
                    "Initialize should queue the default region without synchronous mesh work.");
                Check(!fixture.World.IsColumnReady(0, 0), "Loaded data alone must not imply a ready collider.");
                fixture.World.TickStreaming();
                Check(fixture.World.LoadedCount == 1 && fixture.World.PendingCount == 24,
                    "One update must build exactly one column.");
                Check(fixture.World.IsColumnReady(0, 0), "The closest column must build first.");
                fixture.World.TickStreaming();
                Check(fixture.World.LoadedCount == 2 && fixture.World.PendingCount == 23,
                    "A second update should build only one more column.");
                fixture.World.BuildImmediate();
                Check(fixture.World.LoadedCount == 25 && fixture.World.PendingCount == 0,
                    "Explicit bootstrap must finish the radius-two 25-column neighborhood.");
            }
        }

        private static void ResidencyAndQueueStayBoundedAfterTeleports()
        {
            using (var fixture = new Fixture(true, 2))
            {
                fixture.World.BuildImmediate();
                Set(fixture.Data, new Vector3Int(8, 150, 8), 4);
                fixture.World.NotifyEdit(new Vector3Int(8, 150, 8));
                fixture.World.BuildImmediate();
                fixture.World.SetRadius(99);
                Check(fixture.World.Radius == 4, "Radius must clamp at four.");
                for (int i = 1; i <= 6; i++)
                {
                    fixture.World.Origin = new Vector2Int(i * 16000, -i * 16000);
                    fixture.Player.transform.position = new Vector3(0.5f, 30, 0.5f);
                    fixture.World.Refresh();
                    Check(fixture.World.LoadedCount == 0 && fixture.World.PendingCount == 81,
                        "Teleport must evict old geometry and cancel stale queued columns.");
                    fixture.World.BuildImmediate();
                    Check(fixture.World.LoadedCount == 25 && fixture.World.PendingCount == 56,
                        "Radius-four bootstrap must limit synchronous work to the central 25 columns.");
                    Check(fixture.Data.LoadedColumns <= 121, "Resident data including one-column halo must remain bounded.");
                    fixture.World.Refresh();
                    Check(fixture.World.PendingCount == 56, "Repeated Refresh must not duplicate queue work.");
                    AssertMetrics(fixture.World);
                }
                fixture.World.SetRadius(-10);
                Check(fixture.World.Radius == 1 && fixture.World.LoadedCount <= 9 && fixture.World.PendingCount <= 9,
                    "Shrinking radius must immediately bound active and pending columns.");
                Check(fixture.Data.LoadedColumns <= 25, "Shrinking radius must also evict the old data halo.");
                fixture.World.Origin = Vector2Int.zero;
                fixture.World.Refresh();
                fixture.World.BuildImmediate();
                Check(fixture.Data.GetBlock(8, 150, 8) == 4, "Eviction must preserve edits for a later revisit.");
                Check(fixture.World.IsColumnReady(8, 8), "Revisited edited column must have a ready collider.");
            }
        }

        private static void LargeEditedMeshUses32BitIndices()
        {
            using (var fixture = new Fixture())
            {
                // More than 2,730 separated air cubes, each surrounded by six solid faces.
                for (int y = -180; y <= -130; y += 2)
                    for (int z = 1; z < 16; z += 2)
                        for (int x = 1; x < 16; x += 2)
                            Set(fixture.Data, new Vector3Int(x, y, z), 0);
                for (int y = -126; y <= -90; y += 2)
                    for (int z = 1; z < 16; z += 2)
                        for (int x = 1; x < 16; x += 2)
                            Set(fixture.Data, new Vector3Int(x, y, z), 0);
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, Vector2Int.zero);
                try
                {
                    Check(mesh.vertexCount > 65535, "Fixture must exercise a genuinely large mesh.");
                    Check(mesh.indexFormat == IndexFormat.UInt32, "Large meshes must switch to 32-bit indices before triangles.");
                    int[] triangles = mesh.triangles;
                    int maximum = 0;
                    foreach (int index in triangles)
                    {
                        Check(index >= 0 && index < mesh.vertexCount, "Triangle index is out of range.");
                        maximum = Math.Max(maximum, index);
                    }
                    Check(maximum > 65535, "Index data must address vertices above the 16-bit limit.");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        private static void WorldEdgeFacesStayInBounds()
        {
            using (var fixture = new Fixture())
            {
                Vector2Int column = WorldData.Column(WorldData.WorldMin, WorldData.WorldMax - 1);
                Mesh mesh = ChunkWorld.BuildMesh(fixture.Data, column);
                try { AssertMatchesBlocks(fixture.Data, column, mesh); }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        // Check every emitted face and independently probe a small set of full-depth shafts,
        // surface slabs, bottom cells and edit neighborhoods. Repeated 102,400-voxel scans make
        // the generator-v2 streaming tests unnecessarily expensive.
        private static void AssertMatchesBlocks(WorldData data, Vector2Int column, Mesh mesh)
        {
            HashSet<Face> actual = ReadFaces(column, mesh);
            foreach (Face face in actual)
            {
                Vector3Int neighbor = face.Cell + face.Normal;
                Check(WorldData.IsSolid(data.GetBlock(face.Cell.x, face.Cell.y, face.Cell.z)) &&
                      !WorldData.IsSolid(data.GetBlock(neighbor.x, neighbor.y, neighbor.z)),
                    "Mesh emitted a non-solid or occluded face at " + face.Cell + ".");
            }
            var samples = new HashSet<Vector3Int>();
            int startX = column.x * WorldData.ChunkSize;
            int startZ = column.y * WorldData.ChunkSize;
            for (int z = 0; z < WorldData.ChunkSize; z++)
                for (int x = 0; x < WorldData.ChunkSize; x++)
                {
                    int height = data.SurfaceHeight(startX + x, startZ + z);
                    for (int y = height - 1; y <= height + 1; y++)
                        samples.Add(new Vector3Int(startX + x, y, startZ + z));
                    samples.Add(new Vector3Int(startX + x, WorldData.MinY, startZ + z));
                }
            foreach (Vector2Int shaft in new[] { new Vector2Int(0, 0), new Vector2Int(15, 0),
                new Vector2Int(8, 8), new Vector2Int(0, 15), new Vector2Int(15, 15) })
                for (int y = WorldData.MinY; y < WorldData.MaxY; y++)
                    samples.Add(new Vector3Int(startX + shaft.x, y, startZ + shaft.y));
            foreach (Vector3Int edit in data.EditsNearColumn(column))
            {
                samples.Add(edit);
                foreach (Vector3Int direction in Directions) samples.Add(edit + direction);
            }
            foreach (Vector3Int cell in samples)
            {
                if (WorldData.Column(cell.x, cell.z) != column || !WorldData.IsSolid(data.GetBlock(cell.x, cell.y, cell.z))) continue;
                foreach (Vector3Int direction in Directions)
                {
                    Vector3Int adjacent = cell + direction;
                    if (!WorldData.IsSolid(data.GetBlock(adjacent.x, adjacent.y, adjacent.z)))
                    {
                        Check(actual.Contains(new Face(cell, direction)), "Missing generated/edit face at " + cell + " toward " + direction + ".");
                    }
                }
            }
        }

        private static HashSet<Face> ReadFaces(Vector2Int column, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] indices = mesh.triangles;
            Check(vertices.Length % 4 == 0 && indices.Length == vertices.Length / 4 * 6,
                "Every visible quad must have four vertices and two triangles.");
            Check(normals.Length == vertices.Length, "Every vertex needs an explicit face normal.");
            var result = new HashSet<Face>();
            Vector3 offset = new Vector3(column.x * 16, 0, column.y * 16);
            for (int i = 0; i < vertices.Length; i += 4)
            {
                Vector3 normal = normals[i];
                Check(Mathf.Abs(normal.sqrMagnitude - 1) < 0.001f, "Face normal must be unit length.");
                Vector3 center = (vertices[i] + vertices[i + 1] + vertices[i + 2] + vertices[i + 3]) * 0.25f;
                Vector3Int cell = Vector3Int.FloorToInt(center - normal * 0.5f) + Vector3Int.RoundToInt(offset);
                var face = new Face(cell, Vector3Int.RoundToInt(normal));
                Check(result.Add(face), "Duplicate face emitted for " + cell + ".");
                for (int k = 0; k < 4; k++)
                {
                    Check(normals[i + k] == normal, "Cube face normals must be flat.");
                    Check(vertices[i + k] == (Vector3)Vector3Int.RoundToInt(vertices[i + k]), "Voxel corners must be integer coordinates.");
                }
                int firstTriangle = i / 4 * 6;
                for (int t = firstTriangle; t < firstTriangle + 6; t += 3)
                {
                    for (int j = 0; j < 3; j++)
                        Check(indices[t + j] >= i && indices[t + j] < i + 4, "Quad triangle references another face.");
                    Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                    Check(Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0.99f,
                        "Triangle winding must point along its outward normal.");
                }
            }
            return result;
        }

        private static int CountVertices(WorldData data, Vector2Int column)
        {
            Mesh mesh = ChunkWorld.BuildMesh(data, column);
            try { return mesh.vertexCount; }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static MeshFilter FindColumn(ChunkWorld world, Vector2Int column)
        {
            Vector3 position = world.ToLocal(new Vector3Int(column.x * 16, 0, column.y * 16));
            foreach (MeshFilter filter in world.GetComponentsInChildren<MeshFilter>())
                if (filter.transform.position == position && filter.GetComponent<MeshCollider>() != null) return filter;
            throw new InvalidOperationException("No mesh for column " + column);
        }

        private static void AssertMetrics(ChunkWorld world)
        {
            MeshFilter[] filters = world.GetComponentsInChildren<MeshFilter>();
            Check(filters.Length >= world.LoadedCount && filters.Length <= world.LoadedCount * 2 &&
                world.transform.childCount == world.LoadedCount,
                "Each column has one terrain object and at most one tree batch, with no per-voxel/tree objects.");
            int vertices = 0;
            foreach (MeshFilter filter in filters)
            {
                vertices += filter.sharedMesh.vertexCount;
                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    Check(filter.gameObject.layer == 0, "Terrain must remain raycastable on layer zero.");
                    Check(collider.sharedMesh != null, "Every column requires its authoritative voxel collider.");
                    Check(filter.transform.childCount <= 1, "Vegetation must be a batch, never an object per tree.");
                }
                else Check(filter.name == "Tree sprites" && filter.gameObject.layer == 2,
                    "Sprite batches must remain visual-only and excluded from voxel picking.");
            }
            Check(world.VertexCount == vertices, "VertexCount must equal current mesh vertices.");
            Check(!float.IsNaN(world.LastBuildMs) && !float.IsInfinity(world.LastBuildMs) && world.LastBuildMs >= 0,
                "Build timing must be finite and nonnegative.");
            Check(world.BuiltMaterial != null && world.BuiltMaterial.shader.name == "EchoWorld/VoxelTerrain",
                "Terrain must use the shared built-in-pipeline triplanar shader.");
        }

        private static void Set(WorldData data, Vector3Int cell, byte id, bool requireChange = true)
        {
            bool changed = data.SetBlock(cell.x, cell.y, cell.z, id);
            Check(!requireChange || changed, "Fixture edit did not change " + cell + ".");
            Check(data.GetBlock(cell.x, cell.y, cell.z) == id, "Fixture edit did not reach WorldData.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private readonly struct Face : IEquatable<Face>
        {
            public Vector3Int Cell { get { return cell; } }
            public Vector3Int Normal { get { return normal; } }
            private readonly Vector3Int cell;
            private readonly Vector3Int normal;
            public Face(Vector3Int cell, Vector3Int normal) { this.cell = cell; this.normal = normal; }
            public bool Equals(Face other) { return cell == other.cell && normal == other.normal; }
            public override bool Equals(object value) { return value is Face other && Equals(other); }
            public override int GetHashCode() { unchecked { return cell.GetHashCode() * 397 ^ normal.GetHashCode(); } }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly WorldData Data;
            public readonly ChunkWorld World;
            public readonly GameObject Player;
            private readonly string saveRoot;
            private readonly GameObject root;

            public Fixture(bool withRenderer = false, int radius = 1)
            {
                saveRoot = Path.Combine(Path.GetTempPath(), "echo-world-render-tests-" + Guid.NewGuid().ToString("N"));
                Data = new WorldData(72831, saveRoot);
                if (!withRenderer) return;
                root = new GameObject("RenderingTests.World");
                Player = new GameObject("RenderingTests.Player");
                Player.layer = 2;
                Player.transform.position = new Vector3(0.5f, 30, 0.5f);
                World = root.AddComponent<ChunkWorld>();
                World.FollowTarget = Player.transform;
                World.SetRadius(radius);
                World.Initialize(Data);
            }

            public void Dispose()
            {
                try
                {
                    if (root != null) Object.DestroyImmediate(root);
                    if (Player != null) Object.DestroyImmediate(Player);
                }
                finally
                {
                    if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
                }
            }
        }
    }
}
