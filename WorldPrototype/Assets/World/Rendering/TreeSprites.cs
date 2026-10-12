using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoWorld
{
    /// <summary>One GPU billboard batch per column, backed by the unmodified seasons_trees atlas.</summary>
    internal static class TreeSprites
    {
        internal sealed class Batch
        {
            internal readonly HashSet<Vector3Int> HiddenVoxels = new HashSet<Vector3Int>();
            internal readonly List<Vector3Int> Roots = new List<Vector3Int>();
            internal readonly List<int> Heights = new List<int>();
        }

        internal static Batch Collect(WorldData data, Vector2Int column)
        {
            var batch = new Batch();
            var edits = new List<Vector3Int>(data.EditsNearColumn(column));
            int startX = column.x * WorldData.ChunkSize, startZ = column.y * WorldData.ChunkSize;
            foreach (Vector3Int root in data.TreesInColumn(column))
            {
                int x = root.x - startX, z = root.z - startZ;
                // Border trees deliberately keep their textured voxels. A sprite and its entire
                // replacement canopy then share one streaming owner and change atomically.
                if (x < 2 || x >= WorldData.ChunkSize - 2 || z < 2 || z >= WorldData.ChunkSize - 2) continue;
                bool damaged = false;
                foreach (Vector3Int edit in edits)
                    if (Math.Abs(edit.x - root.x) <= 2 && Math.Abs(edit.z - root.z) <= 2 &&
                        edit.y >= root.y - 1 && edit.y <= root.y + 6) { damaged = true; break; }
                if (damaged) continue;
                int trunk = 0;
                while (trunk < 7 && data.GetBlock(root.x, root.y + trunk, root.z) == 6) trunk++;
                // The v2 generator has 4..6 trunk voxels; an unknown tree shape stays voxel based.
                if (trunk < 4 || trunk > 6) continue;
                batch.Roots.Add(root);
                batch.Heights.Add(trunk + 1);
                for (int y = 0; y <= trunk; y++)
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            bool stem = dx == 0 && dz == 0 && y < trunk;
                            bool crown = y >= trunk - 3 && Math.Abs(dx) + Math.Abs(dz) <= (y == trunk ? 1 : 3);
                            if (!stem && !crown) continue;
                            var cell = root + new Vector3Int(dx, y, dz);
                            byte id = data.GetBlock(cell.x, cell.y, cell.z);
                            if (id == 7 || (id == 6 && stem)) batch.HiddenVoxels.Add(cell);
                        }
            }
            return batch;
        }

        internal static Mesh Build(WorldData data, Vector2Int column, Batch batch)
        {
            if (batch.Roots.Count == 0) return null;
            var vertices = new List<Vector3>();
            var offsets = new List<Vector2>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            var bounds = new Bounds();
            for (int i = 0; i < batch.Roots.Count; i++)
            {
                Vector3Int root = batch.Roots[i];
                // Subtract integer origins before converting to floating point.
                Vector3 anchor = new Vector3(root.x - column.x * WorldData.ChunkSize + 0.5f, root.y,
                    root.z - column.y * WorldData.ChunkSize + 0.5f);
                float height = batch.Heights[i] + 0.35f;
                float halfWidth = height * 0.5f;
                float bottom = -height * 0.045f;
                // Atlas inspected at original 1024x1280: two 320px-wide trees at x=0/384.
                // Summer is y=640 in Unity UV coordinates; winter is y=0.
                int left = ((root.x ^ root.z ^ data.Seed) & 1) == 0 ? 0 : 384;
                int row = data.BiomeAt(root.x, root.z) == BiomeKind.Snow ? 0 : 640;
                float u0 = (left + 0.5f) / 1024f, u1 = (left + 319.5f) / 1024f;
                float v0 = (row + 0.5f) / 1280f, v1 = (row + 319.5f) / 1280f;
                int first = vertices.Count;
                for (int corner = 0; corner < 4; corner++) vertices.Add(anchor);
                offsets.Add(new Vector2(-halfWidth, bottom));
                offsets.Add(new Vector2(-halfWidth, bottom + height));
                offsets.Add(new Vector2(halfWidth, bottom + height));
                offsets.Add(new Vector2(halfWidth, bottom));
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1));
                uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
                var treeBounds = new Bounds(anchor + Vector3.up * (bottom + height * 0.5f),
                    new Vector3(height, height, height));
                if (i == 0) bounds = treeBounds; else bounds.Encapsulate(treeBounds);
            }
            var mesh = new Mesh { name = "Tree sprites " + column.x + "," + column.y };
            try
            {
                mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetUVs(1, offsets);
                mesh.SetTriangles(indices, 0);
                mesh.bounds = bounds; // Bounds include every possible yaw of the displaced quads.
                return mesh;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                else UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }
    }
}
