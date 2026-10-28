"""Integer mission policy over observations. No simulator/world imports.

The frontier planner is new robotics code. MEMORIA-X and WSP persistence are
the original imported implementations; we do not claim the entire grid agent
or a certified flight gate runs here. Continuous geometry belongs to adapter.
"""
from collections import deque
from dataclasses import dataclass
from pathlib import Path
import math

from echoai.echo58.memx.memory import Memory, l1_key
from echoai.echo58.wspfiles.codec import read
from echoai.echo58.wspfiles.memory import export_memory, restore_memory


def ned_to_enu_mm(north, east, down):
    if not all(math.isfinite(v) for v in (north, east, down)):
        raise ValueError('nonfinite estimate')
    return tuple(round(v * 1000) for v in (east, north, -down))


def enu_to_unity(v):
    return v[0], v[2], v[1]


def line(a, b):
    x, y = a
    dx, dy = abs(b[0]-x), abs(b[1]-y)
    sx, sy = (1 if x < b[0] else -1), (1 if y < b[1] else -1)
    err = dx-dy
    while True:
        yield x, y
        if (x, y) == b: return
        e = 2*err
        if e > -dy: err -= dy; x += sx
        if e < dx: err += dx; y += sy


class Grid:
    cell_mm = 500
    bounds = (-8000, -2500, 8000, 10500)

    def __init__(self):
        self.cells, self.seen = {}, {}

    def cell(self, xy): return tuple((v+250)//500 for v in xy[:2])
    def centre(self, c): return c[0]*500, c[1]*500

    def inside(self, c):
        x, y = self.centre(c)
        a, b, d, e = self.bounds
        return a <= x <= d and b <= y <= e

    def ray(self, start_mm, end_mm, hit, stamp):
        cells = list(line(self.cell(start_mm), self.cell(end_mm)))
        for c in cells[:-1] if hit else cells:
            # Static warehouse: a beam above a rack must not erase its footprint.
            # Removing obstacles will require an explicit 3D evidence model.
            if self.inside(c) and self.cells.get(c)!=1:
                self.cells[c], self.seen[c] = 0, stamp
        if hit and self.inside(cells[-1]):
            self.cells[cells[-1]], self.seen[cells[-1]] = 1, stamp

    def safe(self, c):
        # 3x3 observed free neighbourhood: conservative margin for 0.7m X500.
        return all(self.inside((c[0]+x, c[1]+y)) and
                   self.cells.get((c[0]+x, c[1]+y)) == 0
                   for x in (-1, 0, 1) for y in (-1, 0, 1))

    def neighbours(self, c):
        return [(c[0]+x, c[1]+y) for x, y in ((1,0),(0,1),(-1,0),(0,-1))]

    def tree(self, start):
        prev, queue = {start: None}, deque([start])
        while queue:
            c = queue.popleft()
            for n in self.neighbours(c):
                if n not in prev and self.safe(n): prev[n] = c; queue.append(n)
        return prev

    def path(self, start, goal):
        return self.trace(self.tree(start), goal)

    @staticmethod
    def trace(prev, goal):
        if goal not in prev: return []
        result = []
        while goal is not None:
            result.append(goal); goal = prev[goal]
        return result[::-1]


class InspectionMemory:
    def __init__(self, arm, path=None):
        if arm not in ('memx', 'empty', 'cache'): raise ValueError('arm')
        self.arm = arm
        self.memory = Memory()
        if path:
            archive=read(path)
            if archive.provenance.get('coordinate_frame')!='home-observed-px4-enu-v1':
                raise ValueError('incompatible or unspecified memory coordinate frame')
            self.memory=restore_memory(archive)
        self.received, self.cache = set(), {}
        # Cache control sees the same historical observations, if supplied.
        for r in self.memory.records:
            self.cache[r['i']] = self.cache.get(r['i'], 0)+1 if r['o']=='PASS' else 0

    @staticmethod
    def key(xy):
        # Fixed 2m spatial sectors inside public geofence, identities 1..80.
        x, y = xy
        return max(0, min(9, (x+9000)//2000)) + 10*max(0, min(7, (y+3000)//2000)) + 1

    def known(self, xy):
        ident = self.key(xy)
        if self.arm == 'empty': return False
        if self.arm == 'cache': return self.cache.get(ident, 0) >= 3
        # Require local support: L2 cannot make all unvisited sectors familiar.
        if self.memory._rule(self.memory.l1.get(l1_key(ident, 0, 'TOGGLE', 0))) != 'PASS':
            return False
        return self.memory.recall(ident, 0, 'TOGGLE', 0) == 'PASS'

    def receipt(self, xy, scan_ms, success):
        ident = self.key(xy)
        if (ident, scan_ms) in self.received: return
        self.received.add((ident, scan_ms))
        self.memory.observe(ident, 0, 'TOGGLE', 0, 'PASS' if success else 'NOOP')
        self.cache[ident] = self.cache.get(ident, 0)+1 if success else 0

    def save(self, path):
        return export_memory(self.memory, Path(path), provenance={
            'source':'flightlab-observed-inspections', 'scope':'same-map-spatial-sectors',
            'model':'X500', 'calibrated_energy':False,
            'coordinate_frame':'home-observed-px4-enu-v1'})


@dataclass
class Observation:
    time_ms: int
    position_mm: tuple
    rays: list                 # [(end_x_mm, end_y_mm, hit)] from sensor adapter
    scan_ms: int
    pose_ms: int


class Agent:
    def __init__(self, arm='memx', memory=None, budget_ms=120000, max_inspections=8):
        self.grid = Grid()
        self.memory = InspectionMemory(arm, memory)
        self.budget_ms, self.max_inspections = budget_ms, max_inspections
        self.started_ms = None
        self.returning = False
        self.target = None
        self.visited = set()
        self.inspect_since = None
        self.inspections = self.skips = self.replans = 0
        self.distance_mm, self.last_pos, self.last_scan = 0, None, -1
        self.reason = 'awaiting sensors'

    def step(self, obs):
        if self.started_ms is None: self.started_ms = obs.time_ms
        x, y, z = obs.position_mm
        age = max(obs.time_ms-obs.scan_ms, obs.time_ms-obs.pose_ms)
        if age > 1000:
            return self.decision('LAND' if age > 5000 else 'HOLD', (x,y,1500), 'sensor_stale')
        if self.last_pos:
            self.distance_mm += math.isqrt((x-self.last_pos[0])**2+(y-self.last_pos[1])**2)
        self.last_pos = (x, y)
        if obs.scan_ms != self.last_scan:
            for ex, ey, hit in obs.rays: self.grid.ray((x,y), (ex,ey), hit, obs.scan_ms)
            self.last_scan = obs.scan_ms
        cell = self.grid.cell((x,y))
        if not self.grid.inside(cell):
            return self.decision('LAND', (x,y,1500), 'outside_geofence')
        home_path = self.grid.path(cell, (0,0))
        # 1m/s nominal plus conservative return allowance and settling/landing.
        reserve = len(home_path)*900+18000
        if obs.time_ms-self.started_ms+reserve >= self.budget_ms or len(self.visited)>=self.max_inspections:
            self.returning = True
        if self.returning:
            if x*x+y*y <= 400*400:
                return self.decision('LAND', (0,0,1500), 'returned_home')
            if not home_path:
                return self.decision('LAND', (x,y,1500), 'no_observed_return_path')
            return self.navigate(home_path, 'return_reserve')
        if not self.grid.safe(cell):
            # Short wait allows multiple scans to fill unknown holes, then land.
            if obs.time_ms-self.started_ms > 8000:
                self.returning = True
                if len(home_path)>1:
                    return self.navigate(home_path, 'clearance_changed_return')
                return self.decision('LAND', (x,y,1500), 'no_safe_observed_neighbourhood')
            return self.decision('HOLD', (x,y,1500), 'mapping_local_clearance')
        if self.target is not None:
            tx, ty = self.grid.centre(self.target)
            if (x-tx)**2+(y-ty)**2 < 350**2:
                if self.inspect_since is None:
                    if self.memory.known((x,y)):
                        self.skips += 1; self.visited.add(self.target); self.target = None
                        return self.decision('HOLD', (x,y,1500), 'memory_skips_optional_inspection')
                    self.inspect_since = obs.time_ms
                    self.inspections += 1
                self.memory.receipt((x,y), obs.scan_ms, self.grid.safe(cell))
                if obs.time_ms-self.inspect_since >= 1200:
                    self.visited.add(self.target); self.target = None; self.inspect_since = None
                return self.decision('HOLD', (tx,ty,1500), 'inspection_observed_lidar')
            path = self.grid.path(cell, self.target)
            if path: return self.navigate(path, 'exploring_observed_free_space')
            self.memory.receipt((tx,ty), obs.scan_ms, False)
            self.target = None; self.inspect_since = None; self.replans += 1
        tree = self.grid.tree(cell)
        choices = []
        for c in tree:
            dist = abs(c[0]-cell[0])+abs(c[1]-cell[1])
            if dist < 4 or c in self.visited: continue
            if any(abs(c[0]-v[0])+abs(c[1]-v[1]) < 4 for v in self.visited): continue
            unknown = sum((c[0]+dx,c[1]+dy) not in self.grid.cells
                          for dx in range(-4,5) for dy in range(-4,5))
            # Prefer frontiers, then uninspected nearby sectors. No object truth.
            choices.append((unknown*4-dist, c))
        if not choices:
            self.returning = True
            return self.decision('HOLD', (x,y,1500), 'frontiers_complete')
        self.target = max(choices)[1]
        return self.navigate(self.grid.trace(tree, self.target), 'frontier_selected')

    def navigate(self, path, reason):
        # One grid edge at a time, path already clearance checked.
        c = path[min(1,len(path)-1)]
        x,y = self.grid.centre(c)
        return self.decision('NAVIGATE', (x,y,1500), reason)

    def decision(self, mode, target, reason):
        self.reason = reason
        return {'mode':mode, 'target_mm':list(target), 'reason':reason,
                'returning':self.returning, 'inspections':self.inspections,
                'skipped_inspections':self.skips, 'visited':len(self.visited),
                'known_cells':len(self.grid.cells), 'replans':self.replans}
