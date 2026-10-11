"""Synthetic regression: a ray in a cell margin does not clear its wall cube."""

from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "Bridge"))

from navigation import Budget, HypothesisAgent, NavigationAgent, Recorder, validate_observation
from Tools.nav_benchmark import NavigationEnvironment


class NavigationCornerTests(unittest.TestCase):
    def test_grazing_emulator_ray_preserves_occupied_cell_and_blocked_edge(self):
        # Entirely synthetic: no dataset, saved model, or confirmation artifacts.
        walls = {(x, z) for x in range(16) for z in range(16)
                 if x in (0, 15) or z in (0, 15)}
        walls.add((8, 8))
        env = NavigationEnvironment({
            "id": "c-000000000000000000000000",
            "split": "development", "family": "scatter",
            "width": 16, "height": 16,
            "start": {"x": 4, "z": 4}, "goal": {"x": 12, "z": 12},
            "walls": [{"x": x, "z": z} for x, z in sorted(walls)],
            "mapping": [{"x": -1, "z": 0}, {"x": 0, "z": 1},
                        {"x": 1, "z": 0}, {"x": 0, "z": -1}],
        })
        obs = validate_observation(env.observation("corner-regression", 0, "cold"))
        self.assertEqual(obs["yaw"], 0)
        self.assertEqual(len(obs["rays"]), 61)
        self.assertEqual(obs["rays"][55], {
            "sx": 8000, "sz": 8000, "ex": 15037, "ez": 15105, "hit": False,
        })
        # Cell (8,8) starts at 15000 mm; its 1.92 m cube starts at
        # 15040 mm. This range-limited ray enters the margin but stops
        # 3 mm short of the cube's x-face, so it cannot refute that wall.
        with tempfile.TemporaryDirectory(prefix="navigation-corner-") as root:
            journal = Path(root) / "thought.jsonl"
            with Recorder(journal) as recorder:
                agent = NavigationAgent(core=HypothesisAgent(), budget=Budget(2_000_000),
                                        recorder=recorder, journal_path=journal)
                # Seed prior memory to isolate sensor integration. This is
                # not a policy trajectory or an acquisition/learning test.
                edge_key = ((7, 8), 2)
                agent.cells[(8, 8)] = "occupied"
                agent.blocked_edges[edge_key] = {
                    "origin": [7, 8], "command": 2, "target": [8, 8],
                    "infer_cell": True, "rays": [],
                }

                agent._remember(obs)  # Full genuine frame, public fields only.

                self.assertEqual(
                    (agent.cells.get((8, 8)), edge_key in agent.blocked_edges),
                    ("occupied", True),
                    "A grazing ray must preserve occupied memory and its blocked edge",
                )


if __name__ == "__main__":
    unittest.main()
