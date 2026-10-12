"""Private navigation fixtures. Never import this module in a production actor.

Cell coordinates name 1.92 m cube centres on a 2 m grid. Generation validates
connectivity without playing episodes. Seeds live in a separate evaluator-only
manifest, never in observations. Existing datasets are never silently replaced.
"""
import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import random
import secrets
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_WORLDS = ROOT / "Assets/StreamingAssets/Navigation/worlds.json"
DEFAULT_MANIFEST = ROOT / "Artifacts/Navigation/worlds.seed-manifest.json"
FAMILIES = ("rooms", "corridors", "barriers", "scatter")
SPLIT_COUNTS = {"acquisition": 4, "development": 8, "confirmation": 16}
MAPPING = ((-1, 0), (0, 1), (1, 0), (0, -1))  # Environment only.


def encoded(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n").encode()


def digest(value):
    return hashlib.sha256(encoded(value)).hexdigest()


def point(xz):
    return {"x": xz[0], "z": xz[1]}


def reachable(width, height, walls, start):
    seen, queue = {start}, deque([start])
    while queue:
        x, z = queue.popleft()
        for dx, dz in MAPPING:
            cell = x + dx, z + dz
            if 0 <= cell[0] < width and 0 <= cell[1] < height and cell not in walls and cell not in seen:
                seen.add(cell)
                queue.append(cell)
    return seen


def make_case(family, seed, *, split="development", index=0):
    if family not in FAMILIES or split not in SPLIT_COUNTS:
        raise ValueError("unknown family/split")
    rng = random.Random(seed)
    w, h = (rng.randint(15, 16), rng.randint(15, 16)) if split == "acquisition" else (rng.randint(11, 16), rng.randint(11, 16))
    interior = {(x, z) for x in range(1, w-1) for z in range(1, h-1)}
    boundary = {(x, z) for x in range(w) for z in range(h) if x in (0, w-1) or z in (0, h-1)}
    walls = set(boundary)
    if family == "rooms":
        cx, cz = rng.randint(3, w-4), rng.randint(3, h-4)
        walls.update((cx, z) for z in range(1, h-1))
        walls.update((x, cz) for x in range(1, w-1))
        doors = [(cx, rng.randrange(1, cz)), (cx, rng.randrange(cz+1, h-1)),
                 (rng.randrange(1, cx), cz), (rng.randrange(cx+1, w-1), cz)]
        walls.difference_update(doors)
        # Columns change intra-room routes as well as door locations.
        walls.update(cell for cell in sorted(interior) if cell[0] != cx and cell[1] != cz and rng.random() < .045)
    elif family == "corridors":
        # Random spanning maze, plus independently placed loops and widened bays.
        walls.update(interior)
        stack, visited = [(1, 1)], {(1, 1)}
        walls.discard((1, 1))
        while stack:
            x, z = stack[-1]
            choices = [(x+2*dx, z+2*dz) for dx, dz in MAPPING
                       if 0 < x+2*dx < w-1 and 0 < z+2*dz < h-1 and (x+2*dx, z+2*dz) not in visited]
            if not choices:
                stack.pop()
                continue
            target = rng.choice(choices)
            visited.add(target)
            walls.discard(target)
            walls.discard(((x+target[0])//2, (z+target[1])//2))
            stack.append(target)
        for cell in sorted(interior & walls):
            if rng.random() < .12:
                walls.discard(cell)
    elif family == "barriers":
        vertical = bool(rng.getrandbits(1))
        across, along = (w, h) if vertical else (h, w)
        offset = rng.randint(2, 3)
        for number, cross in enumerate(range(offset, across-2, 3)):
            gap = rng.randint(1, 2) if number % 2 == 0 else rng.randint(along-4, along-3)
            for axis in range(1, along-1):
                if axis not in (gap, gap+1):
                    walls.add((cross, axis) if vertical else (axis, cross))
    else:
        density = rng.uniform(.16, .28)
        walls.update(cell for cell in sorted(interior) if rng.random() < density)
    if split == "acquisition":
        start, goal = (7, 7), (12, 12)
        walls.difference_update((x, z) for x in range(4, 11) for z in range(4, 11))
        walls.difference_update((start, goal))
        if goal not in reachable(w, h, walls, start):
            # Clear an environment-only connector; no path is given to the actor.
            x, z = start
            while (x, z) != goal:
                choices = [(x+1,z)] if x < goal[0] else []
                if z < goal[1]:
                    choices.append((x,z+1))
                x, z = rng.choice(choices)
                walls.discard((x, z))
    else:
        free = sorted(interior - walls)
        start = rng.choice(free)
        connected = reachable(w, h, walls, start)
        # Reject tiny isolated pockets without modifying the generated topology.
        if len(connected) < (w*h)//3:
            start = max(free, key=lambda cell: len(reachable(w, h, walls, cell)))
            connected = reachable(w, h, walls, start)
        distances = {cell: abs(cell[0]-start[0])+abs(cell[1]-start[1]) for cell in connected}
        far = max(distances.values())
        goal = rng.choice(sorted(cell for cell in connected if distances[cell] >= max(4, far*3//4)))
    case = {"id": "c-" + digest([seed, split, family, index])[:24], "split": split, "family": family,
            "width": w, "height": h, "start": point(start), "goal": point(goal),
            "walls": [point(cell) for cell in sorted(walls)], "mapping": [point(cell) for cell in MAPPING]}
    validate_case(case)
    return case


def validate_case(case):
    required = set("id split family width height start goal walls mapping".split())
    if set(case) != required or case["split"] not in SPLIT_COUNTS or case["family"] not in FAMILIES:
        raise ValueError("invalid case fields")
    w, h = case["width"], case["height"]
    if type(w) is not int or type(h) is not int or not 5 <= min(w,h) <= max(w,h) <= 16:
        raise ValueError("invalid dimensions")
    def cell(raw):
        if type(raw) is not dict or set(raw) != {"x", "z"} or any(type(raw[k]) is not int for k in raw):
            raise ValueError("invalid coordinate")
        return raw["x"], raw["z"]
    walls = {cell(p) for p in case["walls"]}
    if len(walls) != len(case["walls"]) or any(not(0 <= x < w and 0 <= z < h) for x,z in walls):
        raise ValueError("invalid walls")
    boundary = {(x,z) for x in range(w) for z in range(h) if x in (0,w-1) or z in (0,h-1)}
    if not boundary <= walls:
        raise ValueError("missing boundary walls")
    start, goal = cell(case["start"]), cell(case["goal"])
    if any(p in walls or not(0 <= p[0] < w and 0 <= p[1] < h) for p in (start,goal)) or start == goal:
        raise ValueError("invalid start/goal")
    if goal not in reachable(w,h,walls,start):
        raise ValueError("disconnected start/goal")
    if tuple(cell(p) for p in case["mapping"]) != MAPPING:
        raise ValueError("unexpected environment mapping")
    if case["split"] == "acquisition" and (min(w,h) < 13 or start != (7,7) or goal != (12,12)):
        raise ValueError("invalid acquisition fixture")


def build_dataset(seed):
    cases, records, topologies = [], [], set()
    for split, count in SPLIT_COUNTS.items():
        for index in range(count):
            family = FAMILIES[index % len(FAMILIES)]
            case_seed = int(digest([str(seed), split, index]), 16)
            case = make_case(family, case_seed, split=split, index=index)
            topology = digest([case["width"], case["height"], case["walls"]])
            if topology in topologies:
                raise ValueError("duplicate topology; choose another generation seed")
            topologies.add(topology)
            cases.append(case)
            records.append({"id": case["id"], "split": split, "family": family,
                            "seed": str(case_seed), "index": index, "caseSha256": digest(case)})
    data = {"schema": "echo.navigation.worlds.v1", "cases": cases}
    manifest = {"schema": "echo.navigation.seed-manifest.v1", "rootSeed": str(seed),
                "datasetSha256": hashlib.sha256(encoded(data)).hexdigest(), "counts": SPLIT_COUNTS,
                "generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "cases": records}
    return data, manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_WORLDS)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--seed", type=int, default=None)
    args = parser.parse_args()
    if args.output.exists() or args.manifest.exists():
        parser.error("dataset/manifest already exists; use fresh output paths")
    data, manifest = build_dataset(args.seed if args.seed is not None else secrets.randbits(128))
    for path in (args.output, args.manifest):
        path.parent.mkdir(parents=True, exist_ok=True)
    with args.manifest.open("xb") as target:
        target.write(encoded(manifest))
    with args.output.open("xb") as target:
        target.write(encoded(data))
    print(json.dumps({"dataset": str(args.output), "seedManifest": str(args.manifest),
                      "counts": SPLIT_COUNTS, "datasetSha256": manifest["datasetSha256"]}))


if __name__ == "__main__":
    main()
