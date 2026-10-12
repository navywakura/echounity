# Unity publication — 12 October 2026

This update publishes the current **ECHO-Station 1.0** and the separate
**Echo MMO Prototype BETA 0.0.2** project under `WorldPrototype/`.
The live working projects and their running services were left untouched.
The ECHO brain remains an imported dependency from the official `echoai`
repository, checked at commit `f6062de0462a523296eb2cbb704472f4faa4a022`.

## What is included

Station, NavigationLab and ArachnidLab; C# world/motor/sensor/HUD sources;
original eight-legged arachnid prefab, Blender source, FBX, GLB and export ZIP;
Python bridge with E[6], persistent/revisable CAM, causal web inspector and tests;
resource/offline/conditioning/closure tools; documentation, captures and the
original experiment records, including failed development attempts.

WorldPrototype includes its independent Assets, Packages, ProjectSettings,
tools, documentation, original historical screenshots and explicitly licensed
textures. It is a local playable prototype, not a working multiplayer MMO or
an ECHO NPC experiment. Unverified atlases have documented visual fallbacks.

The October 12 evidence archive retains full JSON/JSONL transcripts and original
SHA-256 chains. Unlike the earlier bounded selection, it also includes the CPU
navigation transcript campaigns and Unity-specific frozen source snapshots.
Historical dependency hash lists are preserved; copied ECHO core implementations
are excluded. [Archive contents and limits](../Evidence/INDEX.md).

## Verification of this export

[Machine-readable report](PUBLICATION-CHECKS-20261012.json) ·
[New development records](../Evidence/Publication-20261012/MANIFEST.json).

| Check | Result |
|---|---|
| Python bridge with official sibling ECHO dependency | 121/121 tests |
| Public scene imported and inspected by Unity 6000.0.84f1 | 22 world + 12 motion checks |
| Physical conditioning / restart / paired control | Passed |
| Public arachnid navigation, cold development maps | 8/8 |
| Public Station with placeholder props and arachnid | 3/3 missions |
| Public-copy offline CPU mission and resource threshold | Passed |
| Public-copy development journals | 18/18 audited |
| Earlier archive checks | 50 hashes, 10 chains, 152 physical plan replays and 23 tamper tests passed |
| Archive relocation checks | 21 tests, including tampering, missing files and namespace escapes |
| Separate WorldPrototype development verification | 52/52 |

This new development pipeline used system **Python 3.14.6**, the public sibling
ECHO checkout and only the portable publication project. It ended with
`valid: true, development_only: true`. It deliberately did **not** open a new
confirmation or tune against the original held-out maps. The original 16/16
confirmation remains the historical result described in the closure report.
The fresh physical run tests the changed public scene rather than relying on
compilation alone. The WorldPrototype screenshots are historical; the new check
is a headless development verification, not a new graphical review.

Run `Tools/check_publication.py` to verify the final distribution manifest,
all original October 12 archive hashes and all 34 original closure journals.
Source hashes are exact for this export; historical frozen manifests are
exact for their original run. They intentionally describe different scopes.

## Publication corrections

- Reused the existing sibling-source importer. New CAM/WSP/QN imports use the
  official `echoai.core` implementation through `echo5.legacy`, preserving
  module identity. No alternate CAM, thought bus or LLM was introduced.
- Launchers and checks use the selected Python interpreter, `UNITY_EDITOR`
  or Unity on PATH, and optional `BLENDER_EDITOR`. They no longer require the
  author's home paths or private virtual environment.
- Station launches through port 8771; the historical NavigationLab remains on
  8770. The public scene now has the arachnid body and basal telemetry.
- Fixed 17 out-of-range placeholder renderer identifiers inherited from the
  earlier public scene. Unity had rejected them as duplicated identifiers and
  could not resolve the robot. Unique signed-64-bit IDs preserve the existing
  props/colliders. World checks and all three physical missions now pass.
- Added strict relocation to the arachnid auditor: original paths are mapped
  into a supplied archive, without falling back to the live original tree.
  Hashes and receipt checks are unchanged; archived evidence is never rewritten.
- Added publication source/archive verification, screenshots with provenance,
  current README/setup instructions and per-project license notices.

## Exclusions

No Library/Temp/build cache, browser profile, credentials, private agent
configuration, raw Unity licensing log, crash dump or private game save is
published. Adventure Forge source graphics and standalone atlases without
confirmed redistribution terms remain local. The original arachnid is CC0;
other source is GPL-3.0-or-later, and redistributable textures retain their
provided CC licenses. [NOTICE](../NOTICE.md).

Publication does not change the scientific policy, certify general-purpose
intelligence or reopen S6. ECHO-Station's measured contract and the game's
unfinished feature set are documented independently.
