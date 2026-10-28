# Flight Lab videos

Actual Gazebo/PX4 development flights rendered in Unity. **Speed refers to playback**, labeled on screen. The original physics, observations, outcomes and frame order are preserved.

| Recording | Playback | Length | Outcome |
|---|---:|---:|---|
| [Full demonstration](ECHO-FLIGHTLAB-DEMO.mp4) | Mixed, labeled | 2:24 | All five cases below |
| [MEMORIA-X](01-memoria.mp4) | 4× | 0:31.75 | 3/4 points, returned home |
| [Without inspection memory](02-sin-memoria.mp4) | 4× | 0:32.04 | 3/4 points, returned home |
| [Simple cache](03-cache.mp4) | 4× | 0:30.29 | 4/4 points, mission complete |
| [New obstacle](04-obstaculo-nuevo.mp4) | 4× | 0:32.79 | 3/4 points, returned home |
| [LiDAR signal lost](05-perdida-lidar.mp4) | 2× | 0:17.58 | Contingency landing, 0/4 points |

[Source recordings](../../FlightLab/Recordings/) · [Metrics and interpretation](../../FlightLab/RESULTS.md) · [Clip hashes and source-frame hashes](manifest.json) · [Combined-video receipt](COMPLETE.json)

The “partial return” side label is clipped in some frames; the top caption displays the full outcome. The simple cache has the best coverage in this set. The videos demonstrate the actual integration, without claiming a memory advantage or real-aircraft certification.
