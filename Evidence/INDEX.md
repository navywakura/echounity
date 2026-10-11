# Historical evidence selection — 2026-10-11

This bounded archive contains **50 original files, 5,239,293 bytes**. Every file
is copied byte-for-byte; [MANIFEST.json](2026-10-11/MANIFEST.json) records relative
source names, sizes and SHA-256 hashes. Nothing here is a new experiment.

| Family | Included | Scope |
|---|---|---|
| Physical navigation R2 | All 7 complete `thought.jsonl` sessions, checkpoint and map files; Unity integration and audit receipts | 4 acquisition + 3 development episodes, including the obstacle intervention |
| Final classic station | All 3 complete `thought.jsonl` sessions named by the final journal audit, integration/world/motion receipts | Irrigation, delivery and detour missions |
| CPU navigation R1/R2 | Selected aggregate reports, source manifests/freezes, acquisition summaries and both dataset seed manifests | Historical summaries; full per-case CPU transcript campaigns are excluded |
| Verification history | Final Python, sensor and world checks; corner regression red/green logs; R1 audit recovery receipt | Documents actual earlier results and the earlier automation failure |

The original `Artifacts/` inventory was **1,479 files / 505,148,447 bytes**:
Navigation 1,435 / 488,954,962; NavigationPhysical 30 / 3,368,981;
Unity 14 / 12,824,504. Receipts selected from the original `Logs/` add a small
amount outside that inventory. The other CPU campaigns, duplicated development
runs, exploratory sessions and unused checkpoints are omitted. Journals included
here are complete; no selected session is shortened or rechained.

Records contain this project's simulator observations, explicit hypotheses,
actions, consequences and hashes. They contain no rendered images, downloaded
artwork, external chat transcripts or model weights. Credential, private-key and
email scans found no matches in this selected set. Historic paths contain the
already public owner name and source locations. They remain in the records so
the original hash chains can be verified; they are provenance, not dependencies
or instructions to access those locations.

From the repository root, with the complete official echoai checkout available:

```sh
python3 -B Tools/check_evidence.py --tamper-tests
```

This checks all archive hashes, all 10 journal chains and the physical family's
152 recorded plans using the existing ECHO-5 auditor. The optional tamper suite
mutates temporary copies only. `--recorded-root` support maps archived references
to this checkout without reading the old absolute paths or altering evidence.
Results go to ignored `Logs/evidence-check.json`.

CPU report paths may name files intentionally omitted from this bounded archive.
These summaries are not a claim that all CPU transcripts can be replayed from
this repository. Frozen hashes describe the historical sources and therefore
do not attest the later portability or placeholder changes. Do not rerun or
instrument the sealed confirmations to validate packaging.

The navigation result measures a motor-effect model and remembered geometric
map. Depth comes from Unity raycasts; the RGB camera is displayed to the human,
not classified by learned vision. ECHO-5 closure is pending. S6 is later work.
