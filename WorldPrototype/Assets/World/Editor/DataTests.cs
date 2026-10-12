using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EchoWorld
{
    /// <summary>Core checks callable by the editor without NUnit or a running scene.</summary>
    public static class DataTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            var failures = new List<Exception>();
            Run(passed, failures, "negative_floor_division_and_columns", Coordinates);
            Run(passed, failures, "world_bounds_and_pinned_bottom", Bounds);
            Run(passed, failures, "all_25_sections_and_height_limits", Sections);
            Run(passed, failures, "same_seed_independent_of_chunk_order", SeedAndOrder);
            Run(passed, failures, "different_seeds_change_terrain", DifferentSeeds);
            Run(passed, failures, "origin_flat_and_eight_step_walk", Origin);
            Run(passed, failures, "sparse_edits_noops_and_pure_surface", Edits);
            Run(passed, failures, "air_and_placed_voxels_survive_save_unload_reload", Persistence);
            Run(passed, failures, "reverted_edits_prune_memory_and_disk", Reversion);
            Run(passed, failures, "queries_do_not_load_columns", QueryOwnership);
            Run(passed, failures, "neighbor_boundary_edits_survive_unloaded_reads", NeighborEdits);
            Run(passed, failures, "malformed_and_wrong_world_deltas_fail_closed", MalformedFiles);
            Run(passed, failures, "atomic_overwrite_is_order_independent", SaveOrder);
            Run(passed, failures, "save_failure_retains_dirty_loaded_data", SaveFailure);
            Run(passed, failures, "astar_unobstructed_and_deterministic", Paths);
            Run(passed, failures, "astar_blocked_cliffs_and_headroom", BlockedPaths);
            Run(passed, failures, "astar_never_ascends_or_cuts_corners", StepPaths);
            Run(passed, failures, "astar_budget_and_world_edges", PathLimits);
            Run(passed, failures, "diagonal_cost_matches_independent_dijkstra", DiagonalOracle);
            Run(passed, failures, "terrestrial_biomes_caves_trees_and_structures", Terrain);
            Run(passed, failures, "generated_face_candidates_cover_caves_and_features", Candidates);
            Run(passed, failures, "generated_features_and_all_materials_survive_reload", FeatureReload);
            Run(passed, failures, "torch_is_not_support_or_an_obstacle", Torches);
            if (failures.Count != 0)
                throw new AggregateException("WorldData checks failed", failures);
            return passed;
        }

        private static void Run(List<string> passed, List<Exception> failures, string name, Action check)
        {
            try { check(); passed.Add(name); }
            catch (Exception error) { failures.Add(new InvalidOperationException(name, error)); }
        }

        // Also permits a standalone compile/run with Unity's managed assemblies, without opening Unity.
        private static int Main()
        {
            try
            {
                foreach (string name in RunAll()) Console.WriteLine("PASS " + name);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Coordinates()
        {
            int[,] cases = { { -1, -1 }, { -16, -1 }, { -17, -2 }, { 0, 0 },
                { 15, 0 }, { 16, 1 }, { int.MinValue, -134217728 }, { int.MaxValue, 134217727 } };
            for (int i = 0; i < cases.GetLength(0); ++i)
                Check(WorldData.FloorDiv(cases[i, 0], 16) == cases[i, 1], "Incorrect mathematical floor division");
            Check(WorldData.Column(-1, -17) == new Vector2Int(-1, -2), "Negative coordinates mapped to wrong column");
            Check(WorldData.Column(-5000000, 4999999) == new Vector2Int(-312500, 312499), "Extreme columns incorrect");
        }

        private static void Bounds()
        {
            var world = new WorldData(17, null);
            int[] edges = { -5000000, -1, 0, 4999999 };
            foreach (int x in edges)
                foreach (int z in edges)
                {
                    Check(world.InBounds(x, -200, z) && world.InBounds(x, 199, z), "Inclusive world boundary rejected");
                    Check(world.GetBlock(x, -200, z) == 2, "Pinned bottom must be rock");
                    Check(!world.SetBlock(x, -200, z, 0), "Bottom was mined");
                    Check(!world.SetBlock(x, -200, z, 5), "Bottom material was replaced");
                }
            int[,] outside = { { -5000001, 0, 0 }, { 5000000, 0, 0 }, { 0, 0, -5000001 },
                { 0, 0, 5000000 }, { 0, -201, 0 }, { 0, 200, 0 },
                { int.MinValue, 0, int.MaxValue }, { 0, int.MaxValue, 0 }, { 0, int.MinValue, 0 } };
            for (int i = 0; i < outside.GetLength(0); ++i)
            {
                int x = outside[i, 0], y = outside[i, 1], z = outside[i, 2];
                Check(!world.InBounds(x, y, z), "Out-of-world voxel accepted");
                Check(world.GetBlock(x, y, z) == 0 && !world.SetBlock(x, y, z, 3), "Out-of-world voxel read or written");
            }
            Check(!world.SetBlock(0, 50, 0, 11) && !world.SetBlock(0, 50, 0, 255), "Unknown material accepted");
            world.LoadColumn(new Vector2Int(int.MinValue, int.MaxValue));
            world.LoadColumn(new Vector2Int(312500, 0));
            world.UnloadColumn(new Vector2Int(-312501, 0));
            Check(world.LoadedColumns == 0 && world.EditCount == 0, "Invalid operations allocated column data");
        }

        private static void Sections()
        {
            WithRoot(root =>
            {
                var world = new WorldData(23, root);
                for (int section = 0; section < 25; ++section)
                {
                    int first = -200 + section * 16, last = first + 15;
                    Check(world.InBounds(0, first, 0) && world.InBounds(0, last, 0), "Section exceeds valid height");
                    int editable = Math.Max(-199, first);
                    Check(world.SetBlock(-1, editable, -1, 10), "Section edit rejected");
                    Check(world.SetBlock(-1, last, -1, 10), "Section end edit rejected");
                }
                Check(world.StandHeight(-1, -1) == 200, "Highest solid block was not reflected in stand height");
                world.SaveAll();
                var restored = new WorldData(23, root);
                for (int section = 0; section < 25; ++section)
                {
                    Check(restored.GetBlock(-1, Math.Max(-199, -200 + section * 16), -1) == 10, "Section start lost on reload");
                    Check(restored.GetBlock(-1, -185 + section * 16, -1) == 10, "Section end lost on reload");
                }
                Check(restored.GetBlock(-1, -200, -1) == 2 && restored.GetBlock(-1, 200, -1) == 0, "Section endpoint leaked");
            });
        }

        private static void SeedAndOrder()
        {
            var first = new WorldData(int.MinValue, null);
            var second = new WorldData(int.MinValue, null);
            Vector2Int[] columns = { new Vector2Int(0, 0), new Vector2Int(-1, -2), new Vector2Int(61, -27),
                new Vector2Int(-312500, 312499), new Vector2Int(312499, -312500) };
            foreach (Vector2Int c in columns) first.LoadColumn(c);
            for (int i = columns.Length - 1; i >= 0; --i) second.LoadColumn(columns[i]);
            foreach (Vector2Int c in columns)
                for (int dz = 0; dz < 16; dz += 3)
                    for (int dx = 0; dx < 16; dx += 3)
                    {
                        int x = c.x * 16 + dx, z = c.y * 16 + dz;
                        int h = first.SurfaceHeight(x, z);
                        Check(h == second.SurfaceHeight(x, z), "Height depends on generation order");
                        Check(h >= -199 && h <= 197, "Generated surface lacks floor or headroom");
                        for (int y = -200; y < 200; ++y)
                        {
                            byte block = first.GetBlock(x, y, z);
                            Check(block == second.GetBlock(x, y, z), "Material depends on generation order");
                            Check(block <= 10, "Generated unknown material");
                            if (y < -64) Check(block == 2, "Deep rock contains a cave or generated ore");
                            if (y < h - 2) Check(block == 0 || block == 2, "Generated ore below terrestrial surface");
                            if (y > h + 8) Check(block == 0, "Decoration exceeds its generated bounds");
                        }
                    }
        }

        private static void DifferentSeeds()
        {
            var first = new WorldData(41, null);
            var second = new WorldData(42, null);
            bool differs = false;
            for (int x = 137; x < 1000; x += 37)
                for (int z = -931; z < -130; z += 43)
                    differs |= first.SurfaceHeight(x, z) != second.SurfaceHeight(x, z);
            Check(differs, "Seed does not affect heightfield");
        }

        private static void Origin()
        {
            foreach (int seed in new[] { 0, 1, -1, int.MinValue, int.MaxValue })
            {
                var world = new WorldData(seed, null);
                int h = world.SurfaceHeight(0, 0);
                for (int x = -10; x <= 10; ++x)
                    for (int z = -10; z <= 10; ++z)
                    {
                        bool house = Math.Abs(x + 7) <= 2 && Math.Abs(z + 6) <= 2 || Math.Abs(x - 8) <= 2 && Math.Abs(z - 7) <= 2;
                        Check(world.SurfaceHeight(x, z) == h && WorldData.IsSolid(world.GetBlock(x, h, z)), "Spawn ground is not flat and solid");
                        Check(world.StandHeight(x, z) == (house ? 5 : 1), "Spawn structure/clearance differs from contract");
                    }
                Check(world.WalkHeight(0, 0, 1) == 1 && world.WalkHeight(7, 0, 1) == 1, "Start or seven-block test route obstructed");
                var path = world.FindPath(new Vector3Int(0, h + 1, 0), new Vector3Int(8, h + 1, 0), 3072);
                Check(path.Count == 9, "Origin does not provide an eight-step path");
                Check(world.LoadedColumns == 0, "Path read implicitly loaded columns");
            }
        }

        private static void Edits()
        {
            var world = new WorldData(53, null);
            int h = world.SurfaceHeight(-1, -1);
            byte original = world.GetBlock(-1, h, -1);
            Check(!world.SetBlock(-1, h, -1, original), "No-op reported as an edit");
            Check(world.LoadedColumns == 1 && world.EditCount == 0, "Valid SetBlock must ensure its column is loaded");
            Check(world.SetBlock(-1, h, -1, 0), "Mining solid did not change it");
            Check(world.EditCount == 1 && world.StandHeight(-1, -1) == h, "Mining failed to lower the top solid");
            Check(world.SurfaceHeight(-1, -1) == h, "Edit changed pure base height");
            Check(world.SetBlock(-1, 70, -1, 4) && world.StandHeight(-1, -1) == 71, "Placed highest solid not used");
            Check(world.SetBlock(-1, 70, -1, 0) && world.EditCount == 1, "Reverting placed block did not prune delta");
            Check(world.SetBlock(-1, h, -1, original) && world.EditCount == 0, "Reverting mined block did not prune delta");
            Check(!world.SetBlock(-1, h, -1, original), "Repeated revert reported success");
            // All editable material IDs round-trip, including air over an implicit solid.
            for (byte id = 0; id <= 10; ++id)
            {
                Check(world.SetBlock(2, -100, 2, id), "Valid block ID was rejected");
                Check(world.GetBlock(2, -100, 2) == id, "Block ID changed on read");
            }
        }

        private static void Persistence()
        {
            WithRoot(root =>
            {
                var world = new WorldData(61, root);
                int h = world.StandHeight(15, 7) - 1;
                Check(world.SetBlock(15, h, 7, 0) && world.SetBlock(15, 70, 7, 10), "Fixture edit failed");
                world.SaveAll();
                world.UnloadColumn(new Vector2Int(0, 0));
                Check(world.LoadedColumns == 0 && world.EditCount == 0, "Unloading retained owned column edits");
                Check(world.GetBlock(15, h, 7) == 0 && world.GetBlock(15, 70, 7) == 10, "Unloaded disk queries lost deltas");
                Check(world.StandHeight(15, 7) == 71 && world.LoadedColumns == 0, "StandHeight lost unloaded edits or loaded a column");
                var restored = new WorldData(61, root);
                restored.LoadColumn(new Vector2Int(0, 0));
                restored.LoadColumn(new Vector2Int(0, 0));
                Check(restored.LoadedColumns == 1 && restored.EditCount == 2, "Reload duplicated or dropped edits");
                Check(restored.GetBlock(15, h, 7) == 0 && restored.GetBlock(15, 70, 7) == 10, "Reload did not restore air and placed material");
                Check(restored.SetBlock(15, 70, 7, 3), "Loaded edit replacement failed");
                restored.UnloadColumn(new Vector2Int(0, 0));
                Check(new WorldData(61, root).GetBlock(15, 70, 7) == 3, "Unload did not flush dirty data");
                RejectInvalidData(() => new WorldData(62, root).GetBlock(15, 70, 7), OnlyFile(root));
            });
        }

        private static void Reversion()
        {
            WithRoot(root =>
            {
                var world = new WorldData(71, root);
                byte original = world.GetBlock(-1, -100, -1);
                Check(world.SetBlock(-1, -100, -1, 0), "Fixture mining failed");
                world.SaveAll();
                world.UnloadColumn(new Vector2Int(-1, -1));
                Check(world.GetBlock(-1, -100, -1) == 0, "Read cache did not see saved mining");
                Check(world.SetBlock(-1, -100, -1, original), "Restoring implicit solid failed");
                Check(world.EditCount == 0, "Restored implicit block retained a delta");
                world.UnloadColumn(new Vector2Int(-1, -1));
                Check(world.GetBlock(-1, -100, -1) == original, "Read cache retained reverted mining");
                Check(new WorldData(71, root).GetBlock(-1, -100, -1) == original, "Disk retained reverted mining");
                Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 0, "Empty delta file was not pruned");
            });
        }

        private static void QueryOwnership()
        {
            WithRoot(root =>
            {
                var world = new WorldData(73, root);
                world.LoadColumn(new Vector2Int(0, 0));
                for (int i = 1; i <= 80; ++i)
                {
                    int x = i * 1021, z = -i * 71;
                    world.GetBlock(x, -20, z);
                    world.GetBlock(x, 199, z);
                    world.StandHeight(x, z);
                    foreach (Vector3Int unused in world.EditsNearColumn(WorldData.Column(x, z)))
                        throw new InvalidOperationException("Unedited column exposed edits");
                }
                Check(world.LoadedColumns == 1 && world.EditCount == 0, "Read queries grew the explicit column cache");
                world.UnloadColumn(new Vector2Int(0, 0));
                Check(world.LoadedColumns == 0, "Explicit unload failed");
            });
        }

        private static void NeighborEdits()
        {
            WithRoot(root =>
            {
                var writer = new WorldData(79, root);
                Vector3Int[] points = { new Vector3Int(-1, -100, 5), new Vector3Int(16, 70, 5),
                    new Vector3Int(5, -100, -1), new Vector3Int(5, 70, 16), new Vector3Int(-1, 70, -1) };
                for (int i = 0; i < points.Length; ++i)
                    Check(writer.SetBlock(points[i].x, points[i].y, points[i].z, (byte)(i % 2 == 0 && i != 4 ? 0 : 5)), "Boundary edit failed");
                writer.SaveAll();
                var reader = new WorldData(79, root);
                reader.LoadColumn(new Vector2Int(0, 0));
                var seen = new HashSet<Vector3Int>(reader.EditsNearColumn(new Vector2Int(0, 0)));
                foreach (Vector3Int p in points) Check(seen.Contains(p), "Unloaded neighbor edit missing from meshing halo");
                Check(reader.GetBlock(-1, -100, 5) == 0 && reader.GetBlock(16, 70, 5) == 5, "Persisted boundary face query used base only");
                Check(reader.LoadedColumns == 1 && reader.EditCount == 0, "Halo reads retained explicit neighboring columns");
                reader.LoadColumn(new Vector2Int(1, 0));
                Check(reader.SetBlock(16, 70, 5, 3), "Live neighbor edit failed");
                Check(reader.GetBlock(16, 70, 5) == 3, "Read cache shadowed a loaded dirty neighbor");
                reader.UnloadColumn(new Vector2Int(1, 0));
                Check(reader.GetBlock(16, 70, 5) == 3, "Read cache shadowed newly saved neighbor");
                Check(reader.LoadedColumns == 1, "Unloaded query reloaded the neighboring column");
            });
        }

        private static void MalformedFiles()
        {
            WithRoot(root =>
            {
                var world = new WorldData(83, root);
                world.SetBlock(3, -199, 0, 0);
                world.SetBlock(4, 190, 0, 5);
                world.SaveAll();
                string file = OnlyFile(root);
                byte[] good = File.ReadAllBytes(file);
                // Format fixtures: six little-endian Int32 header fields, sorted (Int32 key, byte ID), FNV-1a trailer.
                var broken = new List<byte[]>();
                broken.Add(new byte[] { 1, 2, 3 });
                broken.Add(new byte[0]);
                var truncated = new byte[good.Length - 1];
                Array.Copy(good, truncated, truncated.Length);
                broken.Add(truncated);
                var trailing = new byte[good.Length + 1];
                Array.Copy(good, trailing, good.Length);
                broken.Add(trailing);
                var checksum = (byte[])good.Clone();
                checksum[checksum.Length - 1] ^= 1;
                broken.Add(checksum);
                int[,] fields = { { 0, 0 }, { 4, 1 }, { 4, 999 }, { 8, 84 }, { 12, 1 }, { 16, -1 },
                    { 20, -1 }, { 20, int.MaxValue }, { 24, -1 }, { 24, 3 },
                    { 24, 102400 }, { 29, 259 } };
                for (int i = 0; i < fields.GetLength(0); ++i)
                {
                    var bytes = (byte[])good.Clone();
                    PutInt(bytes, fields[i, 0], fields[i, 1]);
                    RefreshChecksum(bytes);
                    broken.Add(bytes);
                }
                var badId = (byte[])good.Clone();
                badId[28] = 255;
                RefreshChecksum(badId);
                broken.Add(badId);
                // A valid first record must not survive an invalid second record.
                var badLast = (byte[])good.Clone();
                badLast[33] = 11;
                RefreshChecksum(badLast);
                broken.Add(badLast);
                foreach (byte[] bytes in broken)
                {
                    File.WriteAllBytes(file, bytes);
                    var reader = new WorldData(83, root);
                    RejectInvalidData(() => reader.GetBlock(3, -199, 0), file);
                    RejectInvalidData(() => reader.LoadColumn(new Vector2Int(0, 0)), file);
                    RejectInvalidData(() => reader.SetBlock(4, 190, 0, 3), file);
                    reader.SaveAll();
                    reader.UnloadColumn(new Vector2Int(0, 0));
                    Check(reader.LoadedColumns == 0 && reader.EditCount == 0, "Rejected delta was retained or made editable");
                    Check(EqualBytes(bytes, File.ReadAllBytes(file)), "Rejected save bytes were modified");
                }
                File.WriteAllBytes(file, good);
                var recovered = new WorldData(83, root);
                Check(recovered.GetBlock(3, -199, 0) == 0 && recovered.GetBlock(4, 190, 0) == 5, "Valid file failed after corruption checks");
            });
        }

        private static void SaveOrder()
        {
            WithRoot(root =>
            {
                string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
                var first = new WorldData(89, a);
                var second = new WorldData(89, b);
                first.SetBlock(1, 60, 1, 3); first.SetBlock(2, 61, 2, 5);
                second.SetBlock(2, 61, 2, 5); second.SetBlock(1, 60, 1, 3);
                first.SaveAll(); second.SaveAll();
                Check(EqualBytes(File.ReadAllBytes(OnlyFile(a)), File.ReadAllBytes(OnlyFile(b))), "Save bytes depend on edit order");
                first.SetBlock(1, 60, 1, 4);
                first.SaveAll(); // Exercise replacement of an existing committed file.
                Check(new WorldData(89, a).GetBlock(1, 60, 1) == 4, "Atomic overwrite failed");
                Check(Directory.GetFiles(a, "*", SearchOption.AllDirectories).Length == 1, "Save left temporary files behind");
            });
        }

        private static void SaveFailure()
        {
            WithRoot(root =>
            {
                string blocked = Path.Combine(root, "blocked");
                var world = new WorldData(97, blocked);
                Check(world.SetBlock(0, 70, 0, 5), "Edit unexpectedly depends on successful disk write");
                File.WriteAllText(blocked, "not a directory");
                bool failed = false;
                try { world.UnloadColumn(new Vector2Int(0, 0)); }
                catch (IOException) { failed = true; }
                catch (UnauthorizedAccessException) { failed = true; }
                Check(failed && world.LoadedColumns == 1 && world.EditCount == 1, "Failed save silently discarded dirty data");
                Check(world.GetBlock(0, 70, 0) == 5, "Failed save damaged live edits");
                File.Delete(blocked);
                world.UnloadColumn(new Vector2Int(0, 0));
                Check(world.LoadedColumns == 0 && new WorldData(97, blocked).GetBlock(0, 70, 0) == 5, "Dirty save could not be retried");
            });
        }

        private static void Paths()
        {
            var world = Platform(0, 4, 0, 4, 180);
            var start = new Vector3Int(0, 181, 0);
            var goal = new Vector3Int(4, 181, 4);
            List<Vector3Int> first = world.FindPath(start, goal, 128);
            Check(first.Count == 5 && Math.Abs(PathCost(first) - 4 * Math.Sqrt(2)) < 1e-9, "Unobstructed diagonal route was not shortest");
            ValidatePath(world, first, start, goal);
            List<Vector3Int> again = world.FindPath(start, goal, 128);
            Check(EqualPath(first, again), "A* ties are nondeterministic");
            Check(world.FindPath(start, start, 1).Count == 1, "Valid zero-step path missing");
            var detour = Platform(0, 4, 0, 2, 180);
            detour.SetBlock(2, 181, 1, 3);
            start = new Vector3Int(0, 181, 1); goal = new Vector3Int(4, 181, 1);
            List<Vector3Int> around = detour.FindPath(start, goal, 128);
            Check(Math.Abs(PathCost(around) - (2 + 2 * Math.Sqrt(2))) < 1e-9, "Obstacle was crossed or shortest detour missed");
            ValidatePath(detour, around, start, goal);
        }

        private static void BlockedPaths()
        {
            var world = Platform(0, 4, 0, 2, 180);
            for (int z = 0; z <= 2; ++z) world.SetBlock(2, 181, z, 3);
            Check(world.FindPath(new Vector3Int(0, 181, 1), new Vector3Int(4, 181, 1), 128).Count == 0, "Path traversed a blocking wall or cliff");
            var headroom = Platform(0, 2, 0, 0, 180);
            headroom.SetBlock(1, 182, 0, 3);
            Check(headroom.FindPath(new Vector3Int(0, 181, 0), new Vector3Int(2, 181, 0), 128).Count == 0, "Path ignored the second headroom cell");
            Check(headroom.FindPath(new Vector3Int(1, 181, 0), new Vector3Int(2, 181, 0), 128).Count == 0, "Path accepted a start under a roof");
            headroom.SetBlock(1, 182, 0, 0);
            headroom.SetBlock(0, 181, 0, 3);
            Check(headroom.FindPath(new Vector3Int(0, 181, 0), new Vector3Int(2, 181, 0), 128).Count == 0, "Path accepted an occupied foot cell");
            var roof = Platform(0, 1, 0, 0, 197);
            Check(roof.FindPath(new Vector3Int(0, 198, 0), new Vector3Int(1, 198, 0), 8).Count == 2, "Last valid two-cell headroom rejected");
            roof.SetBlock(1, 198, 0, 3);
            Check(roof.FindPath(new Vector3Int(0, 198, 0), new Vector3Int(1, 199, 0), 8).Count == 0, "Headroom outside vertical bounds accepted");
            // Two walkable floors at the same X/Z must not be collapsed onto the roof.
            var underground = new WorldData(103, null);
            for (int x = 0; x <= 4; ++x)
            {
                underground.SetBlock(x, -101, 0, 2);
                underground.SetBlock(x, -100, 0, 0); underground.SetBlock(x, -99, 0, 0);
            }
            var a = new Vector3Int(0, -100, 0); var b = new Vector3Int(4, -100, 0);
            var cavePath = underground.FindPath(a, b, 32);
            Check(cavePath.Count == 5 && underground.WalkHeight(2, 0, -100) == -100, "Underground route was forced onto the roof");
            ValidatePath(underground, cavePath, a, b);
        }

        private static void StepPaths()
        {
            var world = new WorldData(107, null);
            world.SetBlock(0, 180, 0, 3); world.SetBlock(1, 181, 0, 3); world.SetBlock(2, 180, 0, 3);
            var start = new Vector3Int(0, 181, 0); var goal = new Vector3Int(2, 181, 0);
            var path = world.FindPath(start, goal, 32);
            Check(path.Count == 0 && world.WalkHeight(1, 0, 181) == WorldData.MinY, "Single-block obstacle was automatically climbed");
            Check(world.FindPath(start, new Vector3Int(1, 182, 0), 32).Count == 0, "Raised destination was automatically climbed");
            start = new Vector3Int(1, 182, 0);
            path = world.FindPath(start, goal, 32);
            Check(path.Count == 2 && world.WalkHeight(2, 0, 182) == 181, "Single-block descent was rejected");
            ValidatePath(world, path, start, goal);
            world.SetBlock(2, 183, 0, 3);
            Check(world.FindPath(start, goal, 32).Count == 0, "Descent crossed an overhang at entry head height");
            var diagonal = new WorldData(109, null);
            diagonal.SetBlock(0, 180, 0, 3); diagonal.SetBlock(1, 180, 1, 3);
            Check(diagonal.FindPath(new Vector3Int(0, 181, 0), new Vector3Int(1, 181, 1), 64).Count == 0, "Unsupported diagonal corner accepted");
            var corner = Platform(0, 1, 0, 1, 180);
            corner.SetBlock(1, 181, 0, 3); corner.SetBlock(0, 181, 1, 3);
            Check(corner.FindPath(new Vector3Int(0, 181, 0), new Vector3Int(1, 181, 1), 64).Count == 0, "Path cut a blocked diagonal corner");
        }

        private static void PathLimits()
        {
            var world = new WorldData(113, null);
            var start = new Vector3Int(0, world.StandHeight(0, 0), 0);
            var goal = new Vector3Int(8, world.StandHeight(8, 0), 0);
            Check(world.FindPath(start, goal, 0).Count == 0 && world.FindPath(start, goal, -1).Count == 0, "Nonpositive search budget accepted");
            Check(world.FindPath(start, goal, 1).Count == 0 && world.FindPath(start, goal, 8).Count == 0, "Search exceeded node budget");
            Check(world.FindPath(start, new Vector3Int(4000000, 1, 0), 16).Count == 0, "Far goal exceeded search budget");
            Check(world.FindPath(start, new Vector3Int(int.MaxValue, 1, 0), 16).Count == 0, "Out-of-bounds goal accepted");
            Check(world.FindPath(new Vector3Int(int.MinValue, 1, 0), start, 16).Count == 0, "Out-of-bounds start accepted");
            Check(world.LoadedColumns == 0, "Search allocated owned column caches");
            var budget = Platform(0, 4, 0, 4, 180);
            Check(budget.FindPath(new Vector3Int(2, 181, 2), new Vector3Int(3, 181, 2), 8).Count == 0, "Budget truncation returned a route instead of rejecting search");
            Check(budget.FindPath(new Vector3Int(2, 181, 2), new Vector3Int(3, 181, 2), 9).Count == 2, "Exact discovery budget rejected valid route");
            var edge = Platform(-5000000, -4999998, 4999999, 4999999, 180);
            start = new Vector3Int(-5000000, 181, 4999999); goal = new Vector3Int(-4999998, 181, 4999999);
            var path = edge.FindPath(start, goal, 16);
            Check(path.Count == 3, "Valid world-edge path rejected");
            ValidatePath(edge, path, start, goal);
            Check(edge.FindPath(start, new Vector3Int(-5000001, 181, 4999999), 16).Count == 0, "Search crossed horizontal world boundary");
        }

        private static WorldData Platform(int minX, int maxX, int minZ, int maxZ, int height)
        {
            var world = new WorldData(101, null);
            for (int x = minX; x <= maxX; ++x)
                for (int z = minZ; z <= maxZ; ++z)
                    Check(world.SetBlock(x, height, z, 3), "Platform fixture failed");
            return world;
        }

        private static double PathCost(List<Vector3Int> path)
        {
            if (path.Count == 0) return double.PositiveInfinity;
            double cost = 0;
            for (int i = 1; i < path.Count; ++i)
                cost += path[i].x != path[i - 1].x && path[i].z != path[i - 1].z ? Math.Sqrt(2) : 1;
            return cost;
        }

        private static void DiagonalOracle()
        {
            const int size = 9;
            var random = new System.Random(12345);
            int reachable = 0;
            for (int trial = 0; trial < 12; ++trial)
            {
                var blocked = new bool[size * size];
                var world = Platform(0, size - 1, 0, size - 1, 180);
                for (int i = 1; i < blocked.Length - 1; ++i)
                    if (trial != 0 && random.Next(100) < 23)
                    { blocked[i] = true; world.SetBlock(i % size, 181, i / size, 3); }
                // Independent flat-grid Dijkstra: no production neighbor, heuristic, height,
                // heap or pathfinding helper is used to compute the expected answer.
                var distance = new double[blocked.Length];
                var done = new bool[blocked.Length];
                for (int i = 1; i < distance.Length; ++i) distance[i] = double.PositiveInfinity;
                for (int count = 0; count < distance.Length; ++count)
                {
                    int best = -1;
                    for (int i = 0; i < distance.Length; ++i)
                        if (!done[i] && (best < 0 || distance[i] < distance[best])) best = i;
                    if (best < 0 || double.IsPositiveInfinity(distance[best])) break;
                    done[best] = true;
                    int x = best % size, z = best / size;
                    for (int dz = -1; dz <= 1; ++dz)
                        for (int dx = -1; dx <= 1; ++dx)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (dx == 0 && dz == 0 || nx < 0 || nz < 0 || nx >= size || nz >= size || blocked[nz * size + nx]) continue;
                            if (dx != 0 && dz != 0 && (blocked[z * size + nx] || blocked[nz * size + x])) continue;
                            int next = nz * size + nx;
                            distance[next] = Math.Min(distance[next], distance[best] + Math.Sqrt(dx * dx + dz * dz));
                        }
                }
                var start = new Vector3Int(0, 181, 0); var goal = new Vector3Int(size - 1, 181, size - 1);
                var path = world.FindPath(start, goal, 256);
                double expected = distance[distance.Length - 1];
                if (double.IsPositiveInfinity(expected)) Check(path.Count == 0, "A* crossed an oracle obstacle");
                else
                {
                    ++reachable;
                    Check(Math.Abs(PathCost(path) - expected) < 1e-9, "A* did not minimize real diagonal cost against Dijkstra");
                    ValidatePath(world, path, start, goal);
                }
            }
            Check(reachable >= 4, "Oracle fixtures did not exercise enough reachable detours");
        }

        private static Vector3Int Tree(WorldData world)
        {
            for (int z = 2; z <= 8; ++z)
                for (int x = 2; x <= 8; ++x)
                    foreach (Vector3Int tree in world.TreesInColumn(new Vector2Int(x, z))) return tree;
            throw new InvalidOperationException("No procedural tree found in fixture region");
        }

        private static void Terrain()
        {
            var world = new WorldData(127, null);
            var biomes = new HashSet<BiomeKind>();
            int tallest = int.MinValue;
            for (int z = -1024; z <= 1024; z += 128)
                for (int x = -1024; x <= 1024; x += 128)
                { biomes.Add(world.BiomeAt(x, z)); tallest = Math.Max(tallest, world.SurfaceHeight(x, z)); }
            Check(biomes.Count == 5 && tallest > 60, "Terrestrial biome variety or mountains missing");
            int air = 0, rock = 0, floors = 0;
            for (int z = -8; z <= 8; z += 4)
                for (int x = -8; x <= 8; x += 4)
                {
                    Check(world.GetBlock(x, -65, z) == 2, "Cave extends below -64");
                    for (int y = -64; y < -4; ++y)
                    {
                        byte id = world.GetBlock(x, y, z);
                        if (id == 0) ++air; else { Check(id == 2, "Underground terrain generated ore"); ++rock; }
                        if (world.WalkHeight(x, z, y) == y) ++floors;
                    }
                }
            Check(air > 20 && rock > 20 && floors > 0, "Procedural caves lack air, enclosing rock or walkable floors");
            Vector3Int tree = Tree(world);
            Check(world.GetBlock(tree.x, tree.y, tree.z) == 6 && world.StandHeight(tree.x, tree.z) > tree.y + 3, "Tree trunk/crown absent from authority");
            bool leaves = false;
            for (int y = tree.y; y <= tree.y + 6; ++y) leaves |= world.GetBlock(tree.x + 1, y, tree.z) == 7;
            Check(leaves, "Tree generated no leaf voxels");
            Check(world.GetBlock(-6, 1, -6) == 10 && world.GetBlock(9, 1, 7) == 10, "Two spawn chests missing");
            Check(world.GetBlock(-8, 2, -5) == 5 && world.GetBlock(7, 2, 6) == 5, "Spawn torches missing");
            Check(world.GetBlock(-7, 4, -6) == 4 && world.GetBlock(8, 4, 7) == 4 && world.GetBlock(6, 0, 5) == 3, "Sci-fi house floor/skylight missing");
            Check(world.WalkHeight(-7, -4, 1) == 1 && world.WalkHeight(8, 5, 1) == 1, "House doors are blocked");
            // Seed 127 has an independent procedural settlement centered at (491,-232).
            int settlementFloor = world.SurfaceHeight(491, -232);
            Check(world.GetBlock(487, settlementFloor + 1, -235) == 10 && world.GetBlock(497, settlementFloor + 1, -229) == 10,
                "Procedural settlements are missing outside the spawn area");
            Check(world.LoadedColumns == 0, "Procedural queries took ownership of columns");
        }

        private static void Candidates()
        {
            var world = new WorldData(127, null);
            var column = new Vector2Int(0, 0);
            var candidates = new HashSet<Vector3Int>(world.GeneratedCandidates(column));
            Check(candidates.Count < 16 * 16 * 200, "Candidate stream became a dense whole-column scan");
            Vector3Int[] directions = { Vector3Int.left, Vector3Int.right, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };
            // One bounded exhaustive oracle includes a house, cave walls/ceilings, and the
            // pinned world's underside. Membership is checked for every exposed base solid.
            for (int z = 0; z < 16; ++z)
                for (int x = 0; x < 16; ++x)
                    for (int y = WorldData.MinY; y < WorldData.MaxY; ++y)
                    {
                        if (!WorldData.IsSolid(world.GetBlock(x, y, z))) continue;
                        foreach (Vector3Int d in directions)
                            if (!WorldData.IsSolid(world.GetBlock(x + d.x, y + d.y, z + d.z)))
                            { Check(candidates.Contains(new Vector3Int(x, y, z)), "Exposed generated face missing from candidates"); break; }
                    }
            Vector3Int tree = Tree(world);
            for (int cz = WorldData.Column(tree.x - 2, tree.z - 2).y; cz <= WorldData.Column(tree.x + 2, tree.z + 2).y; ++cz)
                for (int cx = WorldData.Column(tree.x - 2, tree.z - 2).x; cx <= WorldData.Column(tree.x + 2, tree.z + 2).x; ++cx)
                {
                    candidates = new HashSet<Vector3Int>(world.GeneratedCandidates(new Vector2Int(cx, cz)));
                    for (int z = tree.z - 2; z <= tree.z + 2; ++z)
                        for (int x = tree.x - 2; x <= tree.x + 2; ++x)
                            for (int y = tree.y; y <= tree.y + 6; ++y)
                                if (WorldData.Column(x, z) == new Vector2Int(cx, cz) && (world.GetBlock(x, y, z) == 6 || world.GetBlock(x, y, z) == 7))
                                    Check(candidates.Contains(new Vector3Int(x, y, z)), "Trunk or cross-column leaves omitted from candidates");
                }
            Check(world.LoadedColumns == 0, "Candidate queries retained owned columns");
        }

        private static void FeatureReload()
        {
            WithRoot(root =>
            {
                var world = new WorldData(127, root);
                Vector3Int tree = Tree(world);
                Check(world.SetBlock(tree.x, tree.y, tree.z, 0) && world.SetBlock(9, 1, 7, 0) && world.SetBlock(8, 4, 7, 0), "Generated feature could not be mined");
                Check(world.StandHeight(8, 7) == 1, "Removing a roof left a phantom highest solid");
                for (byte id = 0; id <= 10; ++id) world.SetBlock(id, id == 0 ? -100 : 180, 0, id);
                world.SaveAll();
                var restored = new WorldData(127, root);
                Check(restored.GetBlock(tree.x, tree.y, tree.z) == 0 && restored.GetBlock(9, 1, 7) == 0 && restored.GetBlock(8, 4, 7) == 0, "Mined tree/chest/roof regenerated on reload");
                foreach (Vector3Int remaining in restored.TreesInColumn(WorldData.Column(tree.x, tree.z)))
                    Check(remaining != tree, "Removed trunk retained its vegetation base");
                for (byte id = 0; id <= 10; ++id) Check(restored.GetBlock(id, id == 0 ? -100 : 180, 0) == id, "Material schema did not survive v2 persistence");
                Check(restored.GetBlock(-6, 1, -6) == 10 && restored.GetBlock(-8, 2, -5) == 5, "Unedited generated features changed on reload");
                Check(restored.SetBlock(8, 4, 7, 4), "Generated roof restoration failed");
                restored.SaveAll();
                Check(new WorldData(127, root).StandHeight(8, 7) == 5, "Restoring the implicit roof failed after reload");
            });
        }

        private static void Torches()
        {
            for (byte id = 0; id <= 10; ++id) Check(WorldData.IsSolid(id) == (id != 0 && id != 5), "Block solidity schema changed");
            var world = new WorldData(131, null);
            world.SetBlock(0, 180, 0, 5); world.SetBlock(1, 1, 0, 5); world.SetBlock(2, 2, 0, 5);
            Check(world.StandHeight(0, 0) == 1 && world.WalkHeight(0, 0, 181) == WorldData.MinY, "Torch acted as a supporting solid");
            Check(world.FindPath(new Vector3Int(0, 1, 0), new Vector3Int(3, 1, 0), 128).Count == 4, "Torch blocked walking/headroom");
        }

        private static void ValidatePath(WorldData world, List<Vector3Int> path, Vector3Int start, Vector3Int goal)
        {
            Check(path.Count > 0 && path[0] == start && path[path.Count - 1] == goal, "Path endpoints incorrect");
            for (int i = 0; i < path.Count; ++i)
            {
                Vector3Int p = path[i];
                Check(world.InBounds(p.x, p.y, p.z) && world.InBounds(p.x, p.y + 1, p.z), "Path leaves world");
                Check(WorldData.IsSolid(world.GetBlock(p.x, p.y - 1, p.z)) && !WorldData.IsSolid(world.GetBlock(p.x, p.y, p.z)) &&
                    !WorldData.IsSolid(world.GetBlock(p.x, p.y + 1, p.z)), "Path lacks support or headroom");
                if (i == 0) continue;
                Vector3Int previous = path[i - 1];
                int dx = Math.Abs(previous.x - p.x), dz = Math.Abs(previous.z - p.z);
                Check(Math.Max(dx, dz) == 1 && p.y <= previous.y && p.y >= previous.y - 1, "Illegal path edge");
                Check(world.WalkHeight(p.x, p.z, previous.y) == p.y, "Path exceeds entry clearance");
                if (dx != 0 && dz != 0)
                    Check(world.WalkHeight(p.x, previous.z, previous.y) >= p.y && world.WalkHeight(previous.x, p.z, previous.y) >= p.y,
                        "Diagonal cut a corner");
            }
        }

        private static bool EqualPath(List<Vector3Int> a, List<Vector3Int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool EqualBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i) if (a[i] != b[i]) return false;
            return true;
        }

        private static void RejectInvalidData(Action action, string path)
        {
            try { action(); }
            catch (InvalidDataException error)
            {
                Check(error.Message.Contains(path), "Invalid delta error did not identify the rejected file");
                return;
            }
            throw new InvalidOperationException("Invalid delta was not rejected: " + path);
        }

        private static string OnlyFile(string root)
        {
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Check(files.Length == 1, "Expected one committed column delta");
            return files[0];
        }

        private static void PutInt(byte[] bytes, int offset, int value)
        {
            for (int i = 0; i < 4; ++i) bytes[offset + i] = unchecked((byte)(value >> (i * 8)));
        }

        private static void RefreshChecksum(byte[] bytes)
        {
            uint checksum = 2166136261;
            for (int i = 0; i < bytes.Length - 4; ++i) checksum = unchecked((checksum ^ bytes[i]) * 16777619);
            PutInt(bytes, bytes.Length - 4, unchecked((int)checksum));
        }

        private static void WithRoot(Action<string> check)
        {
            string root = Path.Combine(Path.GetTempPath(), "echo-world-data-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { check(root); }
            finally { Directory.Delete(root, true); }
        }
    }
}
