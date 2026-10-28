# Recorded Flight Lab r1 results

Five sequential descriptive development flights, one run per condition, with a common source version and compatible initial school memory. No LLM/API was used. These are simulator records, not a new scientific examination.

| Condition | Outcome | Points | Inspection pauses started | Memory skips | Obstacle-contact samples | Mission time | Distance |
|---|---|---:|---:|---:|---:|---:|---:|
| MEMORIA-X | Partial, returned home | 3/4 | 0 | 3 | 0 | 110.828 s | 45.553 m |
| No initial inspection memory | Partial, returned home | 3/4 | 4 | 0 | 0 | 112.600 s | 46.397 m |
| Simple cache | Complete, returned home | 4/4 | 4 | 0 | 0 | 102.580 s | 41.508 m |
| New obstacle + MEMORIA-X | Partial, returned home | 3/4 | 2 | 1 | 0 | 116.068 s | 48.277 m |
| LiDAR lost + MEMORIA-X | Contingency landing away from base | 0/4 | 0 | 0 | 0 | 24.032 s | 9.160 m |

Ordinary missions had a 120-second simulated budget and four inspection points. The fault case had a 60-second budget and LiDAR loss after 12 seconds. An inspection count records a started pause; it is not automatically a completed inspection. Contacts count samples, not unique impacts.

All five stored decision audits reproduce exactly; all five physical audits verify takeoff and an active contact channel with positive floor contact. The separate physical judge checks at least 0.8 m observed ascent and three accumulated airborne seconds. These checks do not imply certified real-world safety.

The simple cache completed more points than MEMORIA-X. This set therefore establishes working integration and the shown memory-skipping behavior, not a scientific advantage over the cache. Partial returns and the contingency are displayed in the video and remain in the data.

## Reproduce offline

From the repository root, with a compatible public ECHO checkout configured:

```sh
python3 -B FlightLab/Tools/flightlab.py verify
```

This runs 31 CPU tests, verifies the public inventory and compares decision and physical receipts for all five recordings. It does not re-simulate physics or call an API. Original `core.py` snapshots and their hashes are preserved in each recording. Export changes adapt module entry points, paths and tooling; the measured decision core, telemetry, world authoring and physics judge are unchanged.

The school memory has SHA-256 `8dda59bcccf17ed388776d92a218e2f1b92dd39bf241f155a2d2a9c3e3e760fd`; its 24 observations and local-sector scope are retained. Files under `Recordings/` are the demonstration's data, not development plans or terminal logs.

## Video integrity

[The montage](../Media/FlightLab/ECHO-FLIGHTLAB-DEMO.mp4) contains 3,467 frames at 24 fps, 1280×800, H.264, lasting 144.458333 seconds. Its original SHA-256 is `35c59b9b75dbe04fec7f7aad1abb344a2de44281b3bd4fd11539b495ca129efe`. The four ordinary replays are 4×, the fault replay 2×. See the [clip inventory](../Media/FlightLab/manifest.json).

A minor display issue remains in the already recorded clips: the side-panel label for a partial return is clipped, while the full top caption states “regreso parcial”. It does not change the reported outcome.
