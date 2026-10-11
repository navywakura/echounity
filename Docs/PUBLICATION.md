# Publication copy — 2026-10-11

The live ECHO-Station project was read only. **193 selected source/settings/doc
files (2,173,103 bytes)** were copied with their Unity metadata. A final hash
comparison found every selected live source unchanged. Root README and media
are maintained separately by the main publication task.

The public copy includes `Assets/Echo`, both scenes, both StreamingAssets world
datasets, Unity package manifest/lock, all ProjectSettings, Bridge source/tests,
Tools and original project documentation. It imports the official sibling
`echoai/ECHO-5` source and contains no duplicate ECHO-5/legacy implementation.
The matching official GPL-3.0-or-later license is included, with explicit
third-party exclusions in [NOTICE.md](../NOTICE.md).

## Fresh verification

Results and source hashes: [PUBLICATION-CHECKS.json](PUBLICATION-CHECKS.json).
These checks ran with **system Python 3.14.6** and the staged **sibling official
echoai checkout**, without an ECHO5_SOURCE override or project virtualenv.

| Check | Result |
|---|---|
| `python3 -B Tools/check.py --worker --python-only` | 94 tests passed |
| `python3 -B Tools/check_evidence.py --tamper-tests` | 23 tamper tests passed; all 50 original evidence hashes and 10 complete journal chains verified; 152 recorded physical plans replayed |
| Python parser / tool help | 17 Python files parsed; 10 CLI `--help` checks passed |
| Archived namespace mapping | Valid mapping accepted; 3 foreign/traversal/mismatched paths rejected |
| Unity 6000.0.84f1, `-batchmode -nographics -quit` | Exit 0; source import and compile completed with no C# compiler errors |
| Unity references / scene fallback | No unresolved external asset GUIDs; all own asset metadata present; 17 placeholders; all 285 original box-collider blocks unchanged |

No Unity editor was running when the copy-only batch import began. The command
had no executeMethod and did not enter Play mode, render captures, run physical
tests, start the bridge, or reopen a sealed campaign. Generated Library/Logs are
local and ignored. The new primitive visuals have compiled but have not received
a new rendered review or integrated mission run. Historical scientific results
refer to the original run and source hashes, not this export's packaging changes.

## Publication changes

The full list of modified copies is recorded in `PUBLICATION-CHECKS.json`.

- `Assets/Echo/Editor/StationEditor.cs`: missing sprites now produce an authored
  cube visual. Existing object semantics and parent colliders are unchanged.
- `Assets/Scenes/EchoStation.unity`: 17 sprite visuals replaced with built-in
  cube meshes and the existing Amber material; sprite-facing components removed.
- `ProjectSettings/EditorBuildSettings.asset`: NavigationLab now uses its actual
  existing scene GUID instead of a zero GUID.
- `Bridge/echo_source.py` (new), `Bridge/station_agent.py`, `Tools/launch.py`,
  `Tools/check.py`, `Tools/check_navigation.py`, `Tools/nav_benchmark.py` and
  `Tools/audit_physical_navigation.py`: shared explicit/sibling source discovery,
  system Python, `UNITY_EDITOR` or Unity on PATH, no machine-specific source or
  virtualenv dependency. `check.py --python-only` is a bounded foreground option
  when combined with `--worker`.
- `Bridge/tests/test_nav_worlds.py`: the source-freeze assertion now checks the
  actual imported QN implementation. Its official portable name is `core/qn.py`;
  the former filename-only assertion expected `navi10_qn.py`. The source-freeze
  invariant and algorithm tests are preserved.
- `Tools/audit_physical_navigation.py`, `Tools/test_audit_physical_navigation.py`
  and new `Tools/check_evidence.py`: audit unchanged relocated evidence through
  an explicit recorded-root mapping. Archived paths are never dereferenced and
  archived journals are never rewritten. Tamper mutations stay in temporary copies.
- `Tools/import_pack.py`: optional local import receipts and vendor README now
  go under ignored `Logs/asset-import/`, preserving the historical inventory.
- `Bridge/README.md`, `Bridge/NAVIGATION.md`, `Docs/Navigation/USO.md`: portable
  executable instructions. Historical result documents link to the bounded
  archive and identify their scope. New [REPRODUCE.md](REPRODUCE.md),
  [ASSETS.md](ASSETS.md), this report and [Evidence/INDEX.md](../Evidence/INDEX.md)
  explain setup, licenses, evidence selection and limitations.

## Exclusions and evidence scope

**Adventure Forge artwork is excluded:** no `Assets/Art`, imported third-party
metadata, PNG/FBX art, raw ZIP/AFPack archive or vendor pack README. The default
scenes need no proprietary download. Official attribution, terms links and an
optional authorized local import procedure are in [ASSETS.md](ASSETS.md).
The unchanged `ASSET-INVENTORY.json` contains filenames and hashes only.

No Library, Temp, generated build, Logs directory, user settings, crash dump,
credentials, private keys, virtual environment or personal agent configuration
was copied. `.gitignore` excludes generated and locally imported material.

The original Artifacts tree held **1,479 files / 505,148,447 bytes**. The selection
is **50 original files / 5,239,293 bytes**, including selected Logs receipts:
complete seven-session physical navigation and three-session classic families,
plus CPU aggregate receipts/manifests. Full CPU transcript campaigns, duplicate
development runs and unrelated exploratory sessions were excluded. No included
journal was trimmed or rechained. See [Evidence/INDEX.md](../Evidence/INDEX.md).

The controller consumes simulated raycast depth and odometry; the RGB image is
not learned visual recognition. Motor-effect hypotheses and spatial memory are
the learned/reused components. **ECHO-5 closure is pending; S6 is later work.**
No GitHub push was performed by this preparation task.
