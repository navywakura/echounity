# Flight Lab licenses and attribution

Original Flight Lab code, procedural Unity drone display, warehouse layout, documentation and recorded simulator data: **GPL-3.0-or-later**, matching the repository license.

The physical **X500** model and base meshes originate from [PX4/PX4-gazebo-models](https://github.com/PX4/PX4-gazebo-models), as distributed with the PX4 1.15.4 environment. Their original BSD license texts are retained in `Adapter/echo_flightlab/assets/px4/LICENSE` and in the `x500` / `x500_base` model directories. The adapter adds simulated LiDAR and contact sensors; the upstream mass, inertia and motor configuration are preserved.

Unity and its packages retain their own licenses; only project sources, manifests and authored material definitions are included. Unity editor/runtime build caches are excluded. PX4/Gazebo executables are not redistributed here; the optional runtime uses the official ECHO container recipes and their upstream licenses.

MEMORIA-X/WSP and the existing simulator backend are imported from the official [ECHO-AI source](https://github.com/navywakura/echoai), not copied as another implementation. `ECHO-COMPATIBILITY.json` identifies the tested source and hashes.
