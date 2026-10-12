using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EchoWorld
{
    /// <summary>
    /// Single-threaded local world authority. Only LoadColumn and valid SetBlock calls
    /// retain owned columns; ChunkWorld owns their lifetime and budget. Null/empty
    /// saveRoot is an ephemeral world: unloading then discards its edits.
    /// </summary>
    public sealed class WorldData
    {
        public const int ChunkSize = 16, MinY = -200, MaxY = 200, WorldMin = -5000000, WorldMax = 5000000;

        // The file version also identifies the generator. Bump it if base terrain changes.
        private const int FileMagic = 0x31445745, FileVersion = 2;
        private const int Footprint = ChunkSize * ChunkSize;
        private const int ColumnVolume = Footprint * (MaxY - MinY);
        private const int MaximumEdits = ColumnVolume - Footprint; // bottom layer is pinned
        private const int HeaderBytes = 24, TrailerBytes = 4, RecordBytes = 5;
        private const int ReadCacheSize = 4;
        private static readonly Dictionary<int, byte> EmptyEdits = new Dictionary<int, byte>();
        private static readonly int[] StepX = { 1, 0, -1, 0, 1, -1, -1, 1 };
        private static readonly int[] StepZ = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly double DiagonalCost = Math.Sqrt(2.0);

        private sealed class ColumnData
        {
            internal readonly Dictionary<int, byte> Edits;
            internal readonly TerrainGenerator.GeneratedColumn Generated;
            internal bool Dirty;
            internal ColumnData(Dictionary<int, byte> edits, TerrainGenerator.GeneratedColumn generated)
            { Edits = edits; Generated = generated; }
        }

        private struct ReadEntry
        {
            internal Vector2Int Column;
            internal Dictionary<int, byte> Edits;
        }

        private readonly string saveRoot;
        private readonly TerrainGenerator terrain;
        private readonly Dictionary<Vector2Int, ColumnData> columns = new Dictionary<Vector2Int, ColumnData>();
        // A tiny MRU of validated deltas, including misses. No dense arrays, and no
        // per-voxel allocation once the queried column is here or explicitly loaded.
        private readonly ReadEntry[] readCache = new ReadEntry[ReadCacheSize];
        private int readCount;
        private int editCount;

        public int Seed { get; }
        public int LoadedColumns { get { return columns.Count; } }
        /// <summary>Edits in explicitly loaded columns, not a scan of the entire saved world.</summary>
        public int EditCount { get { return editCount; } }

        public WorldData(int seed, string saveRoot)
        {
            Seed = seed;
            terrain = new TerrainGenerator(seed);
            this.saveRoot = string.IsNullOrEmpty(saveRoot) ? null : Path.GetFullPath(saveRoot);
        }

        /// <summary>Mathematical floor division by a positive divisor, including int.MinValue.</summary>
        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        public static Vector2Int Column(int x, int z)
        {
            return new Vector2Int(FloorDiv(x, ChunkSize), FloorDiv(z, ChunkSize));
        }

        public bool InBounds(int x, int y, int z)
        {
            return HorizontalBounds(x, z) && y >= MinY && y < MaxY;
        }

        private static bool HorizontalBounds(int x, int z)
        {
            return x >= WorldMin && x < WorldMax && z >= WorldMin && z < WorldMax;
        }

        private static bool ValidColumn(Vector2Int column)
        {
            return column.x >= WorldMin / ChunkSize && column.x < WorldMax / ChunkSize &&
                   column.y >= WorldMin / ChunkSize && column.y < WorldMax / ChunkSize;
        }

        /// <summary>Pure terrain envelope, before caves and decorations. Off-world returns MinY - 1.</summary>
        public int SurfaceHeight(int x, int z)
        {
            if (!HorizontalBounds(x, z)) return MinY - 1;
            ColumnData loaded;
            return columns.TryGetValue(Column(x, z), out loaded) ? loaded.Generated.Heights[LocalFootprint(x, z)] : terrain.SurfaceHeight(x, z);
        }

        /// <summary>Terrestrial biome; off-world queries return Meadow without allocating a column.</summary>
        public BiomeKind BiomeAt(int x, int z)
        {
            return HorizontalBounds(x, z) ? terrain.BiomeAt(x, z) : BiomeKind.Meadow;
        }

        /// <summary>Air (0) and torches (5) neither support nor obstruct the player.</summary>
        public static bool IsSolid(byte id) { return id != 0 && id != 5; }

        private TerrainGenerator.GeneratedColumn Generated(Vector2Int column)
        {
            ColumnData loaded;
            return columns.TryGetValue(column, out loaded) ? loaded.Generated : terrain.Column(column);
        }

        /// <summary>Pure base-face candidates; combine with EditsNearColumn and its six-neighbor halo.</summary>
        public IEnumerable<Vector3Int> GeneratedCandidates(Vector2Int column)
        {
            if (!ValidColumn(column)) yield break;
            foreach (Vector3Int candidate in terrain.Candidates(column, Generated)) yield return candidate;
        }

        /// <summary>First trunk voxels rooted in this column whose base has not been removed.</summary>
        public IEnumerable<Vector3Int> TreesInColumn(Vector2Int column)
        {
            if (!ValidColumn(column)) yield break;
            foreach (Vector3Int root in Generated(column).Trees)
                if (GetBlock(root.x, root.y, root.z) == 6) yield return root;
        }

        private static int LocalFootprint(int x, int z)
        {
            // Two's-complement masking is floor-modulo for this power-of-two chunk size.
            return (z & (ChunkSize - 1)) * ChunkSize + (x & (ChunkSize - 1));
        }

        private static int Index(int x, int y, int z)
        {
            return (y - MinY) * Footprint + LocalFootprint(x, z);
        }

        private Dictionary<int, byte> Query(int x, int z, out TerrainGenerator.GeneratedColumn generated)
        {
            Vector2Int column = Column(x, z);
            ColumnData loaded;
            if (columns.TryGetValue(column, out loaded))
            {
                generated = loaded.Generated;
                return loaded.Edits;
            }
            generated = terrain.Column(column);
            return QueryDeltas(column);
        }

        public byte GetBlock(int x, int y, int z)
        {
            if (!InBounds(x, y, z)) return 0;
            if (y == MinY) return 2;
            TerrainGenerator.GeneratedColumn generated;
            Dictionary<int, byte> edits = Query(x, z, out generated);
            byte id;
            return edits.TryGetValue(Index(x, y, z), out id) ? id : generated.Block(x, y, z);
        }

        /// <summary>True only for a real change. Invalid IDs/positions, pinned bottom, and no-ops return false.</summary>
        public bool SetBlock(int x, int y, int z, byte id)
        {
            if (!InBounds(x, y, z) || y == MinY || id > 10) return false;
            Vector2Int column = Column(x, z);
            LoadColumn(column);
            ColumnData data = columns[column];
            int index = Index(x, y, z);
            byte original = data.Generated.Block(x, y, z);
            byte current;
            bool hadEdit = data.Edits.TryGetValue(index, out current);
            if (!hadEdit) current = original;
            if (current == id) return false;
            if (id == original)
            {
                data.Edits.Remove(index);
                --editCount;
            }
            else
            {
                data.Edits[index] = id;
                if (!hadEdit) ++editCount;
            }
            data.Dirty = true;
            return true;
        }

        public void LoadColumn(Vector2Int column)
        {
            if (!ValidColumn(column) || columns.ContainsKey(column)) return;
            Dictionary<int, byte> edits = QueryDeltas(column);
            InvalidateRead(column);
            var data = new ColumnData(ReferenceEquals(edits, EmptyEdits) ? new Dictionary<int, byte>() : edits, terrain.Column(column));
            columns.Add(column, data);
            editCount += data.Edits.Count;
        }

        public void UnloadColumn(Vector2Int column)
        {
            ColumnData data;
            if (!columns.TryGetValue(column, out data)) return;
            SaveColumn(column, data); // A failed write throws; dirty data stays owned and retryable.
            columns.Remove(column);
            editCount -= data.Edits.Count;
            InvalidateRead(column);
        }

        public void SaveAll()
        {
            foreach (KeyValuePair<Vector2Int, ColumnData> entry in columns)
                SaveColumn(entry.Key, entry.Value);
        }

        /// <summary>
        /// Edits inside this column and its one-voxel horizontal halo, including
        /// neighboring mined air. Neighbor reads never implicitly LoadColumn.
        /// </summary>
        public IEnumerable<Vector3Int> EditsNearColumn(Vector2Int column)
        {
            if (!ValidColumn(column)) yield break;
            int minX = column.x * ChunkSize - 1, minZ = column.y * ChunkSize - 1;
            int maxX = minX + ChunkSize + 1, maxZ = minZ + ChunkSize + 1;
            for (int dz = -1; dz <= 1; ++dz)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    var neighbor = new Vector2Int(column.x + dx, column.y + dz);
                    if (!ValidColumn(neighbor)) continue;
                    ColumnData loaded;
                    Dictionary<int, byte> edits = columns.TryGetValue(neighbor, out loaded) ? loaded.Edits : QueryDeltas(neighbor);
                    if (edits.Count == 0) continue;
                    var keys = new List<int>(edits.Keys);
                    keys.Sort();
                    foreach (int key in keys)
                    {
                        int x = neighbor.x * ChunkSize + key % ChunkSize;
                        int z = neighbor.y * ChunkSize + (key / ChunkSize) % ChunkSize;
                        if (x >= minX && x <= maxX && z >= minZ && z <= maxZ)
                            yield return new Vector3Int(x, MinY + key / Footprint, z);
                    }
                }
        }

        /// <summary>Foot Y above the highest solid, including edits; MinY off-world. May return MaxY.</summary>
        public int StandHeight(int x, int z)
        {
            if (!HorizontalBounds(x, z)) return MinY;
            TerrainGenerator.GeneratedColumn generated;
            Dictionary<int, byte> edits = Query(x, z, out generated);
            int footprint = LocalFootprint(x, z);
            int height = generated.Tops[footprint];
            // Only sparse additions can rise above the generated top.
            foreach (KeyValuePair<int, byte> edit in edits)
                if (IsSolid(edit.Value) && edit.Key % Footprint == footprint)
                    height = Math.Max(height, MinY + edit.Key / Footprint);
            byte id;
            while (height > MinY)
            {
                if (!edits.TryGetValue((height - MinY) * Footprint + footprint, out id)) id = generated.Block(x, height, z);
                if (IsSolid(id)) break;
                --height;
            }
            return height + 1;
        }

        private Dictionary<int, byte> QueryDeltas(Vector2Int column)
        {
            if (saveRoot == null) return EmptyEdits;
            for (int i = 0; i < readCount; ++i)
            {
                if (readCache[i].Column != column) continue;
                ReadEntry hit = readCache[i];
                for (int j = i; j > 0; --j) readCache[j] = readCache[j - 1];
                readCache[0] = hit;
                return hit.Edits;
            }
            Dictionary<int, byte> edits = ReadDeltas(column);
            if (readCount < ReadCacheSize) ++readCount;
            for (int i = readCount - 1; i > 0; --i) readCache[i] = readCache[i - 1];
            readCache[0] = new ReadEntry { Column = column, Edits = edits };
            return edits;
        }

        private void InvalidateRead(Vector2Int column)
        {
            for (int i = 0; i < readCount; ++i)
            {
                if (readCache[i].Column != column) continue;
                for (int j = i; j < readCount - 1; ++j) readCache[j] = readCache[j + 1];
                readCache[--readCount] = default(ReadEntry);
                return;
            }
        }

        private string ColumnPath(Vector2Int column)
        {
            return Path.Combine(saveRoot, "column_" + column.x.ToString(CultureInfo.InvariantCulture) + "_" +
                                column.y.ToString(CultureInfo.InvariantCulture) + ".ewd");
        }

        private static uint Checksum(byte[] bytes, int count)
        {
            uint value = 2166136261;
            for (int i = 0; i < count; ++i) value = unchecked((value ^ bytes[i]) * 16777619);
            return value;
        }

        private Dictionary<int, byte> ReadDeltas(Vector2Int column)
        {
            string path = ColumnPath(column);
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                CheckMissingSaveDirectory();
                return EmptyEdits;
            }
            catch (DirectoryNotFoundException)
            {
                CheckMissingSaveDirectory();
                return EmptyEdits;
            }
            // Only absence is normal. Access and other I/O failures propagate without
            // caching an empty replacement or allowing LoadColumn/SetBlock to proceed.
            byte[] bytes;
            using (stream)
            {
                long size = stream.Length;
                if (size < HeaderBytes + TrailerBytes || size > HeaderBytes + TrailerBytes + (long)MaximumEdits * RecordBytes)
                    throw InvalidDelta(path, "file length is outside the column format limits");
                using (var reader = new BinaryReader(stream))
                    bytes = reader.ReadBytes((int)size);
                if (bytes.Length != size) throw InvalidDelta(path, "file was truncated while reading");
            }
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadInt32() != FileMagic) throw InvalidDelta(path, "unrecognized file header");
                int version = reader.ReadInt32();
                if (version != FileVersion) throw InvalidDelta(path, "unsupported version " + version + "; expected " + FileVersion);
                int seed = reader.ReadInt32();
                if (seed != Seed) throw InvalidDelta(path, "world seed " + seed + " does not match expected seed " + Seed);
                int columnX = reader.ReadInt32(), columnZ = reader.ReadInt32();
                if (columnX != column.x || columnZ != column.y)
                    throw InvalidDelta(path, "stored column coordinates do not match the requested column");
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumEdits || bytes.Length != HeaderBytes + TrailerBytes + count * RecordBytes)
                    throw InvalidDelta(path, "invalid edit count or file length mismatch");
                reader.BaseStream.Position = bytes.Length - TrailerBytes;
                if (reader.ReadUInt32() != Checksum(bytes, bytes.Length - TrailerBytes))
                    throw InvalidDelta(path, "checksum mismatch");
                reader.BaseStream.Position = HeaderBytes;
                var edits = new Dictionary<int, byte>(count);
                int previous = Footprint - 1;
                for (int i = 0; i < count; ++i)
                {
                    int key = reader.ReadInt32();
                    byte id = reader.ReadByte();
                    // Sorted unique keys also reject pinned floor edits and out-of-column/height data.
                    if (key <= previous || key >= ColumnVolume)
                        throw InvalidDelta(path, "edit " + i + " has an invalid, pinned, duplicate, or unsorted voxel index");
                    if (id > 10) throw InvalidDelta(path, "edit " + i + " has unknown block ID " + id);
                    previous = key;
                    int x = column.x * ChunkSize + key % ChunkSize;
                    int z = column.y * ChunkSize + (key / ChunkSize) % ChunkSize;
                    int y = MinY + key / Footprint;
                    if (id != Generated(column).Block(x, y, z)) edits.Add(key, id);
                }
                return edits.Count == 0 ? EmptyEdits : edits;
            }
        }

        private static InvalidDataException InvalidDelta(string path, string reason)
        {
            return new InvalidDataException("Invalid world delta '" + path + "': " + reason + ". Original file preserved.");
        }

        private void CheckMissingSaveDirectory()
        {
            // A new save root may not exist yet. File.Exists/Directory.Exists hide
            // permission errors, and a regular file in the path is not normal absence.
            string directory = saveRoot;
            while (!string.IsNullOrEmpty(directory))
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0)
                        throw new IOException("Cannot read world deltas: '" + directory + "' is not a directory.");
                    return;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                directory = Path.GetDirectoryName(directory);
            }
        }

        private void SaveColumn(Vector2Int column, ColumnData data)
        {
            if (!data.Dirty) return;
            if (saveRoot == null) { data.Dirty = false; return; }
            string path = ColumnPath(column);
            if (data.Edits.Count == 0)
            {
                File.Delete(path); // Removing the last delta restores the implicit base atomically.
                data.Dirty = false;
                InvalidateRead(column);
                return;
            }
            Directory.CreateDirectory(saveRoot);
            var keys = new List<int>(data.Edits.Keys);
            keys.Sort();
            byte[] bytes;
            using (var memory = new MemoryStream(HeaderBytes + keys.Count * RecordBytes))
            {
                using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(FileMagic); writer.Write(FileVersion); writer.Write(Seed);
                    writer.Write(column.x); writer.Write(column.y); writer.Write(keys.Count);
                    foreach (int key in keys) { writer.Write(key); writer.Write(data.Edits[key]); }
                }
                bytes = memory.ToArray();
            }
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                        writer.Write(Checksum(bytes, bytes.Length));
                    stream.Flush(true);
                }
                // Same-directory rename/replace: never delete the old committed file first.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                data.Dirty = false;
                InvalidateRead(column);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private sealed class PathNode
        {
            internal Vector3Int Position;
            internal double Cost, Estimate;
            internal int HeapIndex;
            internal bool Closed;
            internal PathNode Parent;
        }

        /// <summary>
        /// Eight-neighbor A* at the supplied foot Y, including underground walkable cells.
        /// Horizontal costs are 1 and sqrt(2); moves may stay level or descend one block.
        /// Both side cells must be traversable for a diagonal. Never climbs or cuts corners.
        /// maxNodes bounds all discovered nodes (including frontier), not just expansions.
        /// Exhausting that budget rejects the search rather than returning a potentially
        /// suboptimal route from a truncated graph. Success includes both endpoints.
        /// </summary>
        public List<Vector3Int> FindPath(Vector3Int start, Vector3Int goal, int maxNodes = 2048)
        {
            var none = new List<Vector3Int>();
            if (maxNodes <= 0 || !InBounds(start.x, start.y, start.z) || !InBounds(goal.x, goal.y, goal.z)) return none;
            int minimumSteps = Math.Max(Math.Max(Math.Abs(start.x - goal.x), Math.Abs(start.z - goal.z)), start.y - goal.y);
            if ((long)minimumSteps + 1 > maxNodes || goal.y > start.y || !Headroom(start) || !Headroom(goal)) return none;
            var known = new Dictionary<Vector3Int, PathNode>();
            var heap = new List<PathNode>();
            var first = new PathNode { Position = start, Estimate = Distance(start.x, start.z, goal) };
            known.Add(start, first);
            Push(heap, first);
            while (heap.Count != 0)
            {
                PathNode current = Pop(heap);
                if (current.Position == goal)
                {
                    for (PathNode node = current; node != null; node = node.Parent) none.Add(node.Position);
                    none.Reverse();
                    return none;
                }
                current.Closed = true;
                for (int direction = 0; direction < StepX.Length; ++direction)
                {
                    int x = current.Position.x + StepX[direction], z = current.Position.z + StepZ[direction];
                    if (!HorizontalBounds(x, z)) continue;
                    int y = WalkHeight(x, z, current.Position.y);
                    if (y == MinY || y < goal.y) continue;
                    bool diagonal = direction >= 4;
                    if (diagonal)
                    {
                        // Each orthogonal leg must be supported and have room at entry
                        // height. Requiring sideY >= destinationY also prevents an implicit
                        // upward step out of a lower corner cell.
                        int sideX = WalkHeight(x, current.Position.z, current.Position.y);
                        int sideZ = WalkHeight(current.Position.x, z, current.Position.y);
                        if (sideX < y || sideZ < y) continue;
                    }
                    var position = new Vector3Int(x, y, z);
                    PathNode next;
                    bool seen = known.TryGetValue(position, out next);
                    if (seen && next.Closed) continue;
                    double cost = current.Cost + (diagonal ? DiagonalCost : 1.0);
                    if (seen)
                    {
                        if (cost >= next.Cost) continue;
                        next.Cost = cost;
                        next.Parent = current;
                        BubbleUp(heap, next.HeapIndex);
                    }
                    else
                    {
                        if (known.Count >= maxNodes) return none;
                        next = new PathNode { Position = position, Cost = cost, Estimate = Distance(x, z, goal), Parent = current };
                        known.Add(position, next);
                        Push(heap, next);
                    }
                }
            }
            return none;
        }

        /// <summary>
        /// Nearest walkable foot Y at nearY or nearY-1, or MinY if neither is usable.
        /// Does not search the surface or step up. A descent also needs clearance at
        /// the original head height while crossing into the destination column.
        /// </summary>
        public int WalkHeight(int x, int z, int nearY)
        {
            if (!HorizontalBounds(x, z) || nearY <= MinY || nearY >= MaxY - 1) return MinY;
            if (Headroom(new Vector3Int(x, nearY, z))) return nearY;
            if (!IsSolid(GetBlock(x, nearY + 1, z)) && Headroom(new Vector3Int(x, nearY - 1, z))) return nearY - 1;
            return MinY;
        }

        private bool Headroom(Vector3Int position)
        {
            return HorizontalBounds(position.x, position.z) && position.y > MinY && position.y < MaxY - 1 &&
                   IsSolid(GetBlock(position.x, position.y - 1, position.z)) &&
                   !IsSolid(GetBlock(position.x, position.y, position.z)) &&
                   !IsSolid(GetBlock(position.x, position.y + 1, position.z));
        }

        private static double Distance(int x, int z, Vector3Int goal)
        {
            // Octile distance is a consistent lower bound even on descending cave routes.
            int dx = Math.Abs(x - goal.x), dz = Math.Abs(z - goal.z);
            return Math.Max(dx, dz) + (DiagonalCost - 1.0) * Math.Min(dx, dz);
        }

        private static bool Before(PathNode a, PathNode b)
        {
            double totalA = a.Cost + a.Estimate, totalB = b.Cost + b.Estimate;
            if (totalA != totalB) return totalA < totalB;
            if (a.Estimate != b.Estimate) return a.Estimate < b.Estimate;
            if (a.Position.x != b.Position.x) return a.Position.x < b.Position.x;
            if (a.Position.z != b.Position.z) return a.Position.z < b.Position.z;
            return a.Position.y < b.Position.y;
        }

        private static void Swap(List<PathNode> heap, int a, int b)
        {
            PathNode temporary = heap[a]; heap[a] = heap[b]; heap[b] = temporary;
            heap[a].HeapIndex = a; heap[b].HeapIndex = b;
        }

        private static void BubbleUp(List<PathNode> heap, int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Before(heap[index], heap[parent])) break;
                Swap(heap, index, parent);
                index = parent;
            }
        }

        private static void Push(List<PathNode> heap, PathNode node)
        {
            node.HeapIndex = heap.Count;
            heap.Add(node);
            BubbleUp(heap, node.HeapIndex);
        }

        private static PathNode Pop(List<PathNode> heap)
        {
            PathNode result = heap[0];
            int last = heap.Count - 1;
            if (last == 0) { heap.RemoveAt(0); return result; }
            heap[0] = heap[last]; heap[0].HeapIndex = 0;
            heap.RemoveAt(last);
            int index = 0;
            while (index < heap.Count / 2)
            {
                int child = index * 2 + 1;
                if (child + 1 < heap.Count && Before(heap[child + 1], heap[child])) ++child;
                if (!Before(heap[child], heap[index])) break;
                Swap(heap, child, index);
                index = child;
            }
            return result;
        }
    }
}
