using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoWorld
{
    /// <summary>Sparse surface candidates, then authoritative GetBlock face occlusion.</summary>
    internal static class VoxelMesher
    {
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, Vector3Int.forward, Vector3Int.back
        };

        // Both triangles (0,1,2) and (0,2,3) have an outward cross product for each face.
        private static readonly Vector3[,] Corners =
        {
            { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
            { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
            { new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
            { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) },
            { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
            { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }
        };

        internal static bool IsValidColumn(Vector2Int column)
        {
            long x = (long)column.x * WorldData.ChunkSize;
            long z = (long)column.y * WorldData.ChunkSize;
            return x >= WorldData.WorldMin && x < WorldData.WorldMax && z >= WorldData.WorldMin && z < WorldData.WorldMax;
        }

        internal static Mesh Build(WorldData data, Vector2Int column)
        {
            return Build(data, column, null, out _);
        }

        // The collision mesh keeps the authoritative voxels even when an intact tree uses a sprite.
        // Both meshes share one candidate traversal; ordinary columns also share the Mesh itself.
        internal static Mesh Build(WorldData data, Vector2Int column, HashSet<Vector3Int> spriteVoxels, out Mesh collision)
        {
            collision = null;
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!IsValidColumn(column)) throw new ArgumentOutOfRangeException(nameof(column));
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2Int neighbor = column + new Vector2Int(dx, dz);
                    if (IsValidColumn(neighbor)) data.LoadColumn(neighbor);
                }

            int size = WorldData.ChunkSize;
            int startX = column.x * size;
            int startZ = column.y * size;
            var candidates = new HashSet<int>();
            foreach (Vector3Int cell in data.GeneratedCandidates(column))
                AddCandidate(candidates, cell.x - startX, cell.y, cell.z - startZ);

            // Include surrounding solids even
            // when the edited voxel itself is now air, including edits in the neighbor column.
            foreach (Vector3Int edit in data.EditsNearColumn(column))
            {
                AddCandidate(candidates, edit.x - startX, edit.y, edit.z - startZ);
                foreach (Vector3Int direction in Directions)
                    AddCandidate(candidates, edit.x + direction.x - startX, edit.y + direction.y, edit.z + direction.z - startZ);
            }

            var ordered = new List<int>(candidates);
            ordered.Sort();
            var visible = new MeshBuffer();
            var physical = spriteVoxels != null && spriteVoxels.Count > 0 ? new MeshBuffer() : visible;
            int plane = size * size;
            foreach (int key in ordered)
            {
                int x = key % size;
                int z = key / size % size;
                int y = key / plane + WorldData.MinY;
                int worldX = startX + x;
                int worldZ = startZ + z;
                byte id = data.GetBlock(worldX, y, worldZ);
                if (!WorldData.IsSolid(id)) continue; // Air/torches do not produce or occlude terrain faces.
                var cell = new Vector3Int(worldX, y, worldZ);
                bool hidden = spriteVoxels != null && spriteVoxels.Contains(cell);
                var minimum = new Vector3(x, y, z);
                for (int face = 0; face < Directions.Length; face++)
                {
                    Vector3Int direction = Directions[face];
                    Vector3Int neighbor = cell + direction;
                    bool solidNeighbor = WorldData.IsSolid(data.GetBlock(neighbor.x, neighbor.y, neighbor.z));
                    bool hiddenNeighbor = spriteVoxels != null && spriteVoxels.Contains(neighbor);
                    bool drawFace = !hidden && (!solidNeighbor || hiddenNeighbor);
                    if (!drawFace && (ReferenceEquals(physical, visible) || solidNeighbor)) continue;
                    Color32 color = BlockColor(id, data.Seed, worldX, y, worldZ, direction.y > 0);
                    float variation = 0.94f + (Hash(data.Seed, worldX, y, worldZ) & 255) / 255f * 0.12f;
                    if (drawFace) visible.AddFace(minimum, face, color, new Vector2(id, variation));
                    if (!ReferenceEquals(physical, visible) && !solidNeighbor)
                        physical.AddFace(minimum, face, color, new Vector2(id, variation));
                }
            }
            Mesh mesh = visible.Create("Terrain " + column.x + "," + column.y);
            try
            {
                collision = ReferenceEquals(physical, visible) ? mesh : physical.Create("Collision " + column.x + "," + column.y);
                return mesh;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                else UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }

        private sealed class MeshBuffer
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color32> colors = new List<Color32>();
            private readonly List<Vector2> materials = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            internal void AddFace(Vector3 minimum, int face, Color32 color, Vector2 material)
            {
                int first = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices.Add(minimum + Corners[face, corner]);
                    normals.Add(Directions[face]);
                    colors.Add(color);
                    materials.Add(material);
                }
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }

            internal Mesh Create(string name)
            {
                var mesh = new Mesh { name = name };
                try
                {
                    mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(vertices);
                    mesh.SetNormals(normals);
                    mesh.SetColors(colors);
                    mesh.SetUVs(0, materials);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateBounds();
                    return mesh; // CPU readable for MeshCollider and editor verification.
                }
                catch
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                    throw;
                }
            }
        }

        private static void AddCandidate(HashSet<int> candidates, int x, int y, int z)
        {
            int size = WorldData.ChunkSize;
            if (x < 0 || x >= size || z < 0 || z >= size || y < WorldData.MinY || y >= WorldData.MaxY) return;
            candidates.Add((y - WorldData.MinY) * size * size + z * size + x);
        }

        private static Color32 BlockColor(byte id, int seed, int x, int y, int z, bool top)
        {
            Color baseColor;
            byte glow = 0;
            switch (id)
            {
                case 1: baseColor = top ? new Color32(74, 112, 47, 255) : new Color32(91, 97, 56, 255); break;
                case 2: baseColor = new Color32(105, 105, 99, 255); break;
                case 3: baseColor = new Color32(112, 127, 133, 255); glow = 5; break;
                case 4: baseColor = new Color32(53, 113, 126, 255); glow = 24; break;
                case 6: baseColor = new Color32(101, 71, 45, 255); break;
                case 7: baseColor = new Color32(48, 91, 39, 255); break;
                case 8: baseColor = new Color32(177, 157, 106, 255); break;
                case 9: baseColor = new Color32(222, 229, 229, 255); break;
                case 10: baseColor = new Color32(129, 92, 52, 255); break;
                default: baseColor = new Color32(110, 110, 110, 255); break;
            }
            uint grain = Hash(seed, x, y, z);
            float brightness = 0.94f + (grain & 255) / 255f * 0.12f;
            if (id == 1 && top)
            {
                uint patch = Hash(seed, WorldData.FloorDiv(x, 6), 0, WorldData.FloorDiv(z, 6));
                float dust = ((patch >> 8) & 255) / 255f * 0.18f;
                baseColor = Color.Lerp(baseColor, new Color32(105, 112, 56, 255), dust);
            }
            baseColor.r *= brightness;
            baseColor.g *= brightness;
            baseColor.b *= brightness;
            Color32 result = baseColor;
            result.a = glow; // Vertex alpha is emission strength; the shader always outputs opaque alpha.
            return result;
        }

        private static uint Hash(int seed, int x, int y, int z)
        {
            unchecked
            {
                uint value = (uint)seed ^ (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)z * 2246822519u;
                value = (value ^ (value >> 13)) * 1274126177u;
                return value ^ (value >> 16);
            }
        }
    }
}
