import tempfile
import unittest
from pathlib import Path
from echo_flightlab import core


class CoreTests(unittest.TestCase):
    def test_axes(self):
        self.assertEqual(core.ned_to_enu_mm(2, 3, -1), (3000, 2000, 1000))
        self.assertEqual(core.enu_to_unity((3, 2, 1)), (3, 1, 2))

    def test_ray_stops_at_observed_wall(self):
        m = core.Grid()
        m.ray((0, 0), (2000, 0), True, 10)
        self.assertEqual(m.cells[(4, 0)], 1)
        self.assertEqual(m.cells[(2, 0)], 0)
        self.assertNotIn((5, 0), m.cells)

    def test_unknown_is_not_a_safe_path(self):
        m = core.Grid()
        self.assertEqual(m.path((0, 0), (4, 0)), [])

    def test_free_ray_does_not_erase_a_static_obstacle(self):
        m=core.Grid()
        m.ray((0,0),(2000,0),True,10)
        m.ray((0,0),(4000,0),False,20)
        self.assertEqual(m.cells[(4,0)],1)
        self.assertFalse(m.safe((4,0)))

    def test_path_routes_round_obstacle(self):
        m = core.Grid()
        for x in range(-5, 6):
            for y in range(-5, 6):
                m.cells[x, y] = 0
        m.cells[0, 0] = 1
        p = m.path((-4, 0), (4, 0))
        self.assertTrue(p)
        self.assertTrue(all(m.safe(c) for c in p))
        self.assertNotIn((0, 0), p)

    def test_stale_sensor_cannot_command_motion(self):
        a = core.Agent()
        d = a.step(core.Observation(2000, (0, 0, 1500), [], 0, 0))
        self.assertEqual(d['mode'], 'HOLD')
        d = a.step(core.Observation(9000, (0, 0, 1500), [], 0, 0))
        self.assertEqual(d['mode'], 'LAND')

    def test_budget_requires_return(self):
        a = core.Agent(budget_ms=10000)
        a.started_ms = 0
        a.step(core.Observation(9500, (0, 0, 1500), [], 9500, 9500))
        self.assertTrue(a.returning)

    def test_inspection_requires_independent_receipts(self):
        m = core.InspectionMemory('memx')
        for _ in range(4): m.receipt((0, 0), 100, True)
        self.assertFalse(m.known((0, 0)))
        m.receipt((0, 0), 200, True)
        m.receipt((0, 0), 300, True)
        self.assertTrue(m.known((0, 0)))
        m.receipt((0, 0), 400, False)
        self.assertFalse(m.known((0, 0)))

    def test_other_sector_does_not_inherit_global_pass(self):
        m = core.InspectionMemory('memx')
        for t in (100, 200, 300): m.receipt((0, 0), t, True)
        self.assertFalse(m.known((4000, 4000)))

    def test_wsp_roundtrip(self):
        with tempfile.TemporaryDirectory() as td:
            m = core.InspectionMemory('memx')
            for t in (100, 200, 300): m.receipt((0, 0), t, True)
            f = Path(td)/'memory.wsp'
            m.save(f)
            n = core.InspectionMemory('memx', f)
            self.assertTrue(n.known((0, 0)))
            self.assertEqual(m.memory.records, n.memory.records)

    def test_memory_from_an_unspecified_coordinate_frame_is_rejected(self):
        from echoai.echo58.wspfiles.memory import export_memory
        with tempfile.TemporaryDirectory() as td:
            f=Path(td)/'old.wsp'
            export_memory(core.Memory(),f)
            with self.assertRaisesRegex(ValueError,'coordinate frame'):
                core.InspectionMemory('memx',f)

    def test_nonfinite_pose_rejected(self):
        with self.assertRaises(ValueError): core.ned_to_enu_mm(float('nan'), 0, 0)

    def test_new_obstacle_invalidates_remembered_motion(self):
        a = core.Agent()
        for x in range(-8,9):
            for y in range(-4,15): a.grid.cells[x,y]=0
        a.target=(4,0)
        for t in (10,20,30): a.memory.receipt((2000,0),t,True)
        a.grid.cells[3,0]=1
        d=a.step(core.Observation(500,(0,0,1500),[],500,500))
        if d['mode']=='NAVIGATE':
            self.assertTrue(a.grid.safe(a.grid.cell(d['target_mm'])))
            self.assertNotEqual(d['target_mm'][:2],[2000,0])
        self.assertGreater(a.replans,0)

    def test_cache_control_can_use_same_history(self):
        with tempfile.TemporaryDirectory() as td:
            m=core.InspectionMemory('memx')
            for t in (1,2,3): m.receipt((0,0),t,True)
            f=Path(td)/'m.wsp'; m.save(f)
            self.assertTrue(core.InspectionMemory('cache',f).known((0,0)))
            self.assertFalse(core.InspectionMemory('empty',f).known((0,0)))

    def test_fully_observed_space_does_not_receive_a_hidden_goal(self):
        a=core.Agent()
        self.assertEqual(a.grid.cells,{})
        self.assertIsNone(a.target)
        self.assertEqual(set(core.Observation.__dataclass_fields__),
                         {'time_ms','position_mm','rays','scan_ms','pose_ms'})

    def test_clearance_change_returns_on_observed_safe_route(self):
        a=core.Agent()
        for x in range(-12,4):
            for y in range(-4,8): a.grid.cells[x,y]=0
        a.started_ms=0
        a.grid.cells[-9,2]=1
        d=a.step(core.Observation(20000,(-4852,455,1500),[],20000,20000))
        self.assertEqual(d['mode'],'NAVIGATE')
        self.assertTrue(a.returning)
        self.assertTrue(a.grid.safe(a.grid.cell(d['target_mm'])))


if __name__ == '__main__': unittest.main()
