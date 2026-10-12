# Portable setup and verification

Use Unity **6000.0.84f1** and Python 3. The original scientific closure used
CPython 3.12.13; publication checks identify their interpreter separately.
The runtime bridge uses only the standard library. Unity and Blender are
separate installations, and the optional resource sampler uses `psutil`.

```sh
git clone https://github.com/navywakura/echoai.git
git clone https://github.com/navywakura/echounity.git
cd echounity
export UNITY_EDITOR=/path/to/Unity/Editor/Unity
python3 -B Tools/check.py --worker --python-only
python3 -B Tools/start_arachnid.py --no-blender
```

The checked official ECHO dependency is commit
`f6062de0462a523296eb2cbb704472f4faa4a022`. Its complete source tree is required,
including `ECHO-5/src/echo5`, `nexus0`, `core` and `echo_arc/observe.py`.
The importer uses a sibling `echoai` checkout by default. Set `ECHOAI_SOURCE`
to another complete checkout, or `ECHO5_SOURCE` to its ECHO-5 directory.
No legacy project environment or duplicate implementation is installed.

The arachnid bridge uses **127.0.0.1:8771** for navigation, Station missions,
E[6]/CAM and the passive browser inspector. `--bridge-only` keeps Unity manual;
`--station` opens the three station missions. `BLENDER_EDITOR` selects Blender
when launching without `--no-blender`. On Hyprland the launcher requests
workspace 2 for Unity and workspace 3 for Blender.

`Tools/launch.py` still starts the historical NavigationLab service on 8770.
Its `--classic` option now routes Station to the unified arachnid service on
8771. Do not connect a Station client expecting basal telemetry to the old
service. Launchers do not stop other projects or services.

## Inspect the saved evidence

```sh
python3 -B Tools/check_evidence.py --tamper-tests
python3 -B Tools/check_publication.py
```

The first command verifies the original October 11 selection and its physical
plan replay. The second checks the full published source manifest, October 12
archive hashes and the 34 closure journals through an explicit original-root
mapping. These are read-only audits, not new missions. Historic absolute paths
remain byte-identical inside hash chains and are not dereferenced on the host.

## New development and confirmation

Close the root project in Unity, then:

```sh
python3 -B Tools/check.py --integration --development-only
```

This creates a unique directory under ignored `Artifacts/Closure/`, returns a
background PID and runs Python, Unity geometry, physical conditioning,
development maps, Station missions, resource measurement and an offline CPU
mission. It requires Linux with user/network namespaces enabled, `ip`, and
`psutil` available to the selected interpreter. It does not disable the host
network. See [closure scope](Arachnid/CLOSURE-1.0.md).

`python3 -B Tools/check.py --integration` additionally freezes sources and
opens 16 fresh confirmation cases **only after development passes**. Do not
use confirmation for tuning. Archived examinations are never reopened.
`--worker` keeps the command in the foreground for CI/log collection.

The public Station uses geometric props in place of unbundled artwork.
Its arachnid body is already integrated. The editor method
`EchoStation.Arachnid.Editor.ClosureEditor.PreparePublicProject` can perform
that integration and the read-only geometry checks on an existing scene.

## Separate World Prototype

Open `WorldPrototype/` in Unity Hub, or run:

```sh
python3 WorldPrototype/Tools/world.py verify
python3 WorldPrototype/Tools/world.py open
```

This local voxel prototype does not use the ECHO dependency yet. Its tests and
screenshots are separate from Station's cognitive experiments. See its README
for asset fallbacks, controls and the explicitly unfinished multiplayer scope.
