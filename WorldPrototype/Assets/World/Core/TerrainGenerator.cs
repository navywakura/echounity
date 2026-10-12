using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoWorld
{
    public enum BiomeKind { Meadow, Forest, Desert, Snow, Mountain }

    /// <summary>
    /// Pure v2 terrain. Integer lattice noise is independent of load order and Unity's RNG.
    /// Cave air is compressed into vertical spans once per column; voxel/face queries never
    /// scan the world's height. The small LRU also bounds incidental, unloaded-world reads.
    /// </summary>
    internal sealed class TerrainGenerator
    {
        private const int Size = WorldData.ChunkSize, Area = Size * Size;
        internal const int CacheLimit = 32, CaveBottom = -64;
        private readonly int seed;
        private readonly Dictionary<Vector2Int, LinkedListNode<GeneratedColumn>> cache =
            new Dictionary<Vector2Int, LinkedListNode<GeneratedColumn>>();
        private readonly LinkedList<GeneratedColumn> recent = new LinkedList<GeneratedColumn>();

        internal struct AirSpan
        {
            internal int First, Last;
            internal AirSpan(int first, int last) { First = first; Last = last; }
        }

        internal sealed class GeneratedColumn
        {
            internal readonly Vector2Int Position;
            internal readonly int[] Heights = new int[Area], Tops = new int[Area];
            internal readonly BiomeKind[] Biomes = new BiomeKind[Area];
            internal readonly List<AirSpan>[] Caves = new List<AirSpan>[Area];
            internal readonly Dictionary<int, byte> Features = new Dictionary<int, byte>();
            internal readonly List<Vector3Int> Trees = new List<Vector3Int>();
            internal GeneratedColumn(Vector2Int position) { Position = position; }

            internal byte Block(int x, int y, int z)
            {
                int i = Footprint(x, z);
                byte feature;
                if (Features.TryGetValue((y - WorldData.MinY) * Area + i, out feature)) return feature;
                int height = Heights[i];
                if (y > height) return 0;
                List<AirSpan> spans = Caves[i];
                if (spans != null && y >= CaveBottom)
                    foreach (AirSpan span in spans)
                    {
                        if (y < span.First) break;
                        if (y <= span.Last) return 0;
                    }
                if (height - y > 2) return 2;
                switch (Biomes[i])
                {
                    case BiomeKind.Desert: return 8;
                    case BiomeKind.Snow: return 9;
                    case BiomeKind.Mountain: return height >= 85 && y == height ? (byte)9 : (byte)2;
                    default: return 1;
                }
            }
        }

        private struct Settlement
        {
            internal int X, Z, Height;
            internal Settlement(int x, int z, int height) { X = x; Z = z; Height = height; }
        }

        internal TerrainGenerator(int seed) { this.seed = seed; }

        internal int SurfaceHeight(int x, int z)
        {
            bool protectedGround;
            return Height(x, z, Settlements(x, z, x, z), out protectedGround);
        }

        internal BiomeKind BiomeAt(int x, int z) { return Biome(x, z); }

        internal GeneratedColumn Column(Vector2Int position)
        {
            LinkedListNode<GeneratedColumn> node;
            if (cache.TryGetValue(position, out node))
            {
                recent.Remove(node);
                recent.AddFirst(node);
                return node.Value;
            }
            GeneratedColumn result = Generate(position);
            if (cache.Count == CacheLimit)
            {
                cache.Remove(recent.Last.Value.Position);
                recent.RemoveLast();
            }
            cache.Add(position, recent.AddFirst(result));
            return result;
        }

        internal static int Footprint(int x, int z) { return (z & (Size - 1)) * Size + (x & (Size - 1)); }

        private uint Hash(int x, int y, int z, uint salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ salt;
                value ^= (uint)x * 0x9e3779b9u;
                value ^= (uint)y * 0x85ebca6bu;
                value ^= (uint)z * 0xc2b2ae35u;
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }

        private int Value(int x, int y, int z, uint salt) { return (int)(Hash(x, y, z, salt) % 2001) - 1000; }
        private static int Lerp(int a, int b, int t, int scale) { return (a * (scale - t) + b * t) / scale; }

        private int Noise(int x, int z, int scale, uint salt)
        {
            int cx = WorldData.FloorDiv(x, scale), cz = WorldData.FloorDiv(z, scale);
            int lx = x - cx * scale, lz = z - cz * scale;
            return Lerp(Lerp(Value(cx, 0, cz, salt), Value(cx + 1, 0, cz, salt), lx, scale),
                Lerp(Value(cx, 0, cz + 1, salt), Value(cx + 1, 0, cz + 1, salt), lx, scale), lz, scale);
        }

        private int CaveNoise(int x, int y, int z, uint salt)
        {
            const int horizontal = 24, vertical = 12;
            int cx = WorldData.FloorDiv(x, horizontal), cy = WorldData.FloorDiv(y, vertical), cz = WorldData.FloorDiv(z, horizontal);
            int lx = x - cx * horizontal, ly = y - cy * vertical, lz = z - cz * horizontal;
            int low = Lerp(Lerp(Value(cx, cy, cz, salt), Value(cx + 1, cy, cz, salt), lx, horizontal),
                Lerp(Value(cx, cy, cz + 1, salt), Value(cx + 1, cy, cz + 1, salt), lx, horizontal), lz, horizontal);
            int high = Lerp(Lerp(Value(cx, cy + 1, cz, salt), Value(cx + 1, cy + 1, cz, salt), lx, horizontal),
                Lerp(Value(cx, cy + 1, cz + 1, salt), Value(cx + 1, cy + 1, cz + 1, salt), lx, horizontal), lz, horizontal);
            return Lerp(low, high, ly, vertical);
        }

        private bool Cave(int x, int y, int z)
        {
            // Broad chambers joined by the intersections of two narrow noise bands.
            int chamber = CaveNoise(x, y, z, 0x6a09e667u);
            return chamber > 350 || (Math.Abs(chamber) < 100 && Math.Abs(CaveNoise(x, y, z, 0xbb67ae85u)) < 230);
        }

        private BiomeKind Biome(int x, int z)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) <= 10) return BiomeKind.Meadow;
            if (Noise(x, z, 192, 0x314159u) > 230) return BiomeKind.Mountain;
            int temperature = Noise(x, z, 160, 0x271828u), moisture = Noise(x, z, 128, 0x161803u);
            if (temperature < -230) return BiomeKind.Snow;
            if (temperature > 200 && moisture < 120) return BiomeKind.Desert;
            return moisture > 80 ? BiomeKind.Forest : BiomeKind.Meadow;
        }

        private int NaturalHeight(int x, int z)
        {
            int height = 14 + Noise(x, z, 128, 0x19a53u) / 65 + Noise(x, z, 32, 0x8bd31u) / 110 +
                Noise(x, z, 8, 0xc471du) / 350 + Math.Max(0, Noise(x, z, 192, 0x314159u) - 160) / 6;
            return Math.Max(-12, Math.Min(150, height));
        }

        private List<Settlement> Settlements(int minX, int minZ, int maxX, int maxZ)
        {
            const int spacing = 192;
            var result = new List<Settlement>();
            for (int z = WorldData.FloorDiv(minZ - 20, spacing); z <= WorldData.FloorDiv(maxZ + 20, spacing); ++z)
                for (int x = WorldData.FloorDiv(minX - 20, spacing); x <= WorldData.FloorDiv(maxX + 20, spacing); ++x)
                {
                    if (Hash(x, 0, z, 0xa54ff53au) % 5 != 0) continue;
                    int sx = x * spacing + 24 + (int)(Hash(x, 1, z, 0x510e527fu) % 144);
                    int sz = z * spacing + 24 + (int)(Hash(x, 2, z, 0x9b05688cu) % 144);
                    if (Math.Max(Math.Abs(sx), Math.Abs(sz)) < 64) continue;
                    result.Add(new Settlement(sx, sz, NaturalHeight(sx, sz)));
                }
            return result;
        }

        private int Height(int x, int z, List<Settlement> sites, out bool protectedGround)
        {
            int distance = Math.Max(Math.Abs(x), Math.Abs(z));
            protectedGround = distance <= 10;
            if (protectedGround) return 0;
            int height = NaturalHeight(x, z);
            // Only the radius-ten landing plain is flat; its edge blends into the terrain.
            if (distance < 34) height = height * (distance - 10) / 24;
            foreach (Settlement site in sites)
            {
                int d = Math.Max(Math.Abs(x - site.X), Math.Abs(z - site.Z));
                if (d <= 12) { protectedGround = true; return site.Height; }
                if (d < 20) return Lerp(site.Height, height, d - 12, 8);
            }
            return height;
        }

        private GeneratedColumn Generate(Vector2Int position)
        {
            var result = new GeneratedColumn(position);
            int minX = position.x * Size, minZ = position.y * Size;
            List<Settlement> sites = Settlements(minX - 4, minZ - 4, minX + Size + 3, minZ + Size + 3);
            for (int z = minZ; z < minZ + Size; ++z)
                for (int x = minX; x < minX + Size; ++x)
                {
                    int i = Footprint(x, z);
                    bool protectedGround;
                    int height = Height(x, z, sites, out protectedGround);
                    result.Heights[i] = result.Tops[i] = height;
                    result.Biomes[i] = Biome(x, z);
                    int roof = protectedGround ? height - 4 : height;
                    int first = int.MinValue;
                    for (int y = CaveBottom; y <= roof + 1; ++y)
                    {
                        bool air = y <= roof && Cave(x, y, z);
                        if (air && first == int.MinValue) first = y;
                        if (!air && first != int.MinValue)
                        {
                            if (result.Caves[i] == null) result.Caves[i] = new List<AirSpan>();
                            result.Caves[i].Add(new AirSpan(first, y - 1));
                            first = int.MinValue;
                        }
                    }
                }

            // Anchor trees on a world-space grid, including roots outside the column whose
            // crowns cross its boundary. No neighbor generation or mutable RNG is involved.
            for (int gz = WorldData.FloorDiv(minZ - 8, 8); gz <= WorldData.FloorDiv(minZ + Size + 1, 8); ++gz)
                for (int gx = WorldData.FloorDiv(minX - 8, 8); gx <= WorldData.FloorDiv(minX + Size + 1, 8); ++gx)
                {
                    int x = gx * 8 + 1 + (int)(Hash(gx, 0, gz, 0x1f83d9abu) % 6);
                    int z = gz * 8 + 1 + (int)(Hash(gx, 1, gz, 0x5be0cd19u) % 6);
                    if (x + 2 < minX || x - 2 >= minX + Size || z + 2 < minZ || z - 2 >= minZ + Size ||
                        x < WorldData.WorldMin || x >= WorldData.WorldMax || z < WorldData.WorldMin || z >= WorldData.WorldMax) continue;
                    BiomeKind biome = Biome(x, z);
                    int density = biome == BiomeKind.Forest ? 75 : biome == BiomeKind.Meadow ? 16 : biome == BiomeKind.Snow ? 12 : 0;
                    if (Hash(x, 0, z, 0x428a2f98u) % 100 >= density || Math.Max(Math.Abs(x), Math.Abs(z)) <= 16) continue;
                    bool protectedGround;
                    int ground = Height(x, z, sites, out protectedGround);
                    if (protectedGround || Cave(x, ground, z)) continue;
                    int trunk = 4 + (int)(Hash(x, 0, z, 0x71374491u) % 3);
                    if (x >= minX && x < minX + Size && z >= minZ && z < minZ + Size)
                        result.Trees.Add(new Vector3Int(x, ground + 1, z));
                    for (int y = ground + 1; y <= ground + trunk; ++y) Feature(result, x, y, z, 6);
                    for (int y = ground + trunk - 2; y <= ground + trunk + 1; ++y)
                        for (int dz = -2; dz <= 2; ++dz)
                            for (int dx = -2; dx <= 2; ++dx)
                                if (Math.Abs(dx) + Math.Abs(dz) <= (y == ground + trunk + 1 ? 1 : 3))
                                    Feature(result, x + dx, y, z + dz, 7);
                }
            House(result, -7, -6, 0, 1);
            House(result, 8, 7, 0, -1);
            foreach (Settlement site in sites)
            {
                House(result, site.X - 5, site.Z - 3, site.Height, 1);
                House(result, site.X + 5, site.Z + 3, site.Height, -1);
            }
            return result;
        }

        private static void Feature(GeneratedColumn column, int x, int y, int z, byte id)
        {
            if (WorldData.Column(x, z) != column.Position || y <= WorldData.MinY || y >= WorldData.MaxY) return;
            int i = Footprint(x, z), key = (y - WorldData.MinY) * Area + i;
            if ((id == 6 || id == 7) && y <= column.Heights[i]) return;
            byte existing;
            if (id == 7 && column.Features.TryGetValue(key, out existing) && existing == 6) return;
            column.Features[key] = id;
            if (WorldData.IsSolid(id)) column.Tops[i] = Math.Max(column.Tops[i], y);
        }

        private static void House(GeneratedColumn column, int cx, int cz, int floor, int doorZ)
        {
            int minX = column.Position.x * Size, minZ = column.Position.y * Size;
            if (cx + 2 < minX || cx - 2 >= minX + Size || cz + 2 < minZ || cz - 2 >= minZ + Size) return;
            for (int dz = -2; dz <= 2; ++dz)
                for (int dx = -2; dx <= 2; ++dx)
                {
                    Feature(column, cx + dx, floor, cz + dz, 3);
                    Feature(column, cx + dx, floor + 4, cz + dz, (byte)(dx == 0 && dz == 0 ? 4 : 3));
                    if (Math.Abs(dx) != 2 && Math.Abs(dz) != 2) continue;
                    for (int y = 1; y <= 3; ++y)
                    {
                        if (dx == 0 && dz == 2 * doorZ && y <= 2) continue;
                        byte material = (byte)(y == 2 && Math.Abs(dx) + Math.Abs(dz) < 4 ? 4 : 3);
                        Feature(column, cx + dx, floor + y, cz + dz, material);
                    }
                }
            Feature(column, cx + 1, floor + 1, cz, 10);
            Feature(column, cx - 1, floor + 2, cz + doorZ, 5);
        }

        internal IEnumerable<Vector3Int> Candidates(Vector2Int position, Func<Vector2Int, GeneratedColumn> getColumn)
        {
            GeneratedColumn center = getColumn(position);
            int minX = position.x * Size, minZ = position.y * Size;
            // Capture the stencil once, including at world boundaries. Edits are deliberately
            // separate: the mesher adds EditsNearColumn and their six neighbors itself.
            var neighbors = new GeneratedColumn[4];
            int[] dx = { -1, 1, 0, 0 }, dz = { 0, 0, -1, 1 };
            for (int d = 0; d < 4; ++d)
            {
                var p = new Vector2Int(position.x + dx[d], position.y + dz[d]);
                if ((long)p.x * Size >= WorldData.WorldMin && (long)p.x * Size < WorldData.WorldMax &&
                    (long)p.y * Size >= WorldData.WorldMin && (long)p.y * Size < WorldData.WorldMax) neighbors[d] = getColumn(p);
            }
            var keys = new HashSet<int>(center.Features.Keys);
            for (int z = minZ; z < minZ + Size; ++z)
                for (int x = minX; x < minX + Size; ++x)
                {
                    int i = Footprint(x, z), height = center.Heights[i];
                    AddRange(keys, i, WorldData.MinY, WorldData.MinY);
                    AddRange(keys, i, height, height);
                    if (center.Caves[i] != null)
                        foreach (AirSpan span in center.Caves[i])
                        {
                            AddRange(keys, i, span.First - 1, span.First - 1);
                            if (span.Last < height) AddRange(keys, i, span.Last + 1, span.Last + 1);
                        }
                    for (int d = 0; d < 4; ++d)
                    {
                        int nx = x + dx[d], nz = z + dz[d], ni = Footprint(nx, nz);
                        GeneratedColumn neighbor = nx >= minX && nx < minX + Size && nz >= minZ && nz < minZ + Size ? center : neighbors[d];
                        if (neighbor == null) { AddRange(keys, i, WorldData.MinY, height); continue; }
                        AddRange(keys, i, neighbor.Heights[ni] + 1, height);
                        if (neighbor.Caves[ni] != null)
                            foreach (AirSpan span in neighbor.Caves[ni]) AddRange(keys, i, span.First, Math.Min(height, span.Last));
                    }
                }
            var ordered = new List<int>(keys);
            ordered.Sort();
            foreach (int key in ordered)
            {
                int x = minX + key % Size, y = WorldData.MinY + key / Area, z = minZ + key / Size % Size;
                // Neighbor air spans can overlap this column's air. Filter them before the
                // mesher performs authoritative edit/disk lookups. Torches remain enumerable.
                if (center.Block(x, y, z) != 0) yield return new Vector3Int(x, y, z);
            }
        }

        private static void AddRange(HashSet<int> keys, int footprint, int first, int last)
        {
            first = Math.Max(WorldData.MinY, first); last = Math.Min(WorldData.MaxY - 1, last);
            for (int y = first; y <= last; ++y) keys.Add((y - WorldData.MinY) * Area + footprint);
        }
    }
}
