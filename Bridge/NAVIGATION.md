# Learned navigation backend

Contract: `Docs/Navigation/PROTOCOL.md`; design: `Docs/Navigation/DESIGN.md`.
This adapter imports the installed ECHO-5 `HypothesisAgent`, `Budget`, and
`Recorder`. Frozen source files are read only and bytecode writes are disabled.
It accepts depth rays, odometry, a user goal, and physical receipts. It does not
import Unity worlds, evaluator datasets, command mappings, or held-out maps.

## API and artifact separation

```python
from navigation import NavigationService

service = NavigationService(artifact_root, model_path=None)
decision_json_bytes = service.process(observation_json_bytes)
terminal_json_bytes = service.finish(final_observation_json_bytes)
service.close()
```

`process` and `finish` accept UTF-8 JSON **bytes** and return JSON **bytes**.
`ProtocolError` exposes `status` and `code`; the HTTP wrapper returns that
status and `{"error": code}`. Construction creates no sessions or files.
`close()` is idempotent, closes recorders, and disables new requests. It does
not fabricate an episode completion or save an unfinished motor acquisition.

The existing server keeps `/decide`, `/finish`, and `/health` and adds
`POST /navigate` and `POST /navigate/finish`. Its default physical-acquisition
root is `<project>/Artifacts/Navigation`; the default model
is `model.json` in that root. Start it using:

```sh
cd Bridge  # from the echounity project root
python3 -B server.py
```

For a fresh physical acquisition, give the new server process an empty run root:

```sh
python3 -B server.py \
  --port 8770 --navigation-root "../Artifacts/NavigationPhysical/<run>"
```

Replace `<run>` with a fresh run identifier. `--navigation-root` changes only
navigation artifacts and its default model path;
it does not alter the HTTP API or legacy station artifact configuration.

**CPU evaluators must supply their own artifact root and model path.** There
is no fallback from an explicit evaluator path to the server's physical model,
or between service roots. Reusing an explicit model path deliberately transfers
that evidence. Do not describe a CPU-generated checkpoint as physical Unity
acquisition. `make_server(port=..., service=...)` remains compatible; an
optional `navigation_service=` injects a separately configured navigation API.

## Profiles

| Profile | Motor model | Online motor learning | Motor save | Spatial memory |
| --- | --- | --- | --- | --- |
| `learn` | Existing explicit model, or empty | Yes | On completed, valid episode with new evidence | Fresh session; persist measured map and inferences |
| `trained` | Required checkpoint | No | No | Fresh session; persist for a subsequent revisit |
| `cold` | Empty, ignores checkpoint | Yes | No | Session only; no map persistence |
| `no_memory` | Required checkpoint | No | No | Discard spatial measurements and collision inferences on each observation; no persistence |
| `remembered` | Required checkpoint | No | No | Restore and persist this world's map |

Missing trained checkpoints raise `409 trained_model_required`. Corrupt or
non-navigation checkpoints raise `409 invalid_navigation_model` (or a specific
navigation-model validation code). Restoration uses the real core's evidence
replay; after restoration, frozen profiles never call `observe` on new receipts.
A confirmed motion that disagrees with a frozen effect returns `wait` without
changing the checkpoint. `modelHash` identifies the **core checkpoint**, not the
map or explanatory UI text.

World memory lives under `artifact_root/maps/<sha256(world)>.json`. Dimensions
must match on restore. Map files contain measured cells and separately declared
failed-edge inferences, never actuator training weights. Only `remembered`
loads them; `learn` and `trained` start with empty spatial memory even when a
previous visit exists. Model and map writes use atomic replacement. Exact
retries perform no additional writes. Concurrent acquisition writers within a
service are rejected; a changed model file is never silently overwritten by an
episode that loaded an earlier version. Use one service writer per artifact root.

## Evidence and learning

A motor sample must match the pending proposal's session, sequence, action,
command, starting cell, and current odometry. It must report `arrived`, residual
distance at most **200 mm**, and Manhattan displacement exactly **one cell**.
Blocked, cancelled, diagonal, stationary, multi-cell, mismatched, and rounded
partial arrivals do not teach a command effect. An uncertain changed pose stops
the episode. Blocked motion at the starting cell permits exploration/replanning.

For each qualifying motion, the adapter calls the real `observe` with two
observations on an **empty 16×16 calibration plane**, positions `[z,x]` copied
from the actual starting/arrival cells, generic actions `[0,1,2,3]`, fixed
placeholder goal `[0,0]`, and ticks **0→1**. This is an actuator-identification
projection, not a statement that physical space has no walls. The Recorder event
`navigation_calibration` retains the physical receipt and both projected states
together. Duplicate projections add no support, including duplicates restored
from checkpoint history. The adapter conservatively excludes identical old
projections even across dynamics epochs.

Support and displayed hypotheses come from `checkpoint()['payload']['model']`.
`learnedActions` counts singleton effects with at least two distinct supporting
receipts, matching the core planner's support threshold. The adapter never sets
private support counts or manufactures effects. Supported planning calls the
real `plan` **without `effect_prior`**. Unknown/occupied/inferred cells become
temporary planner exclusions, explicitly labelled as such in the journal.
Candidate frontier selection and calibration experiments are programmed control
logic; the core supplies routes and action sequences from acquired effects.

The saved model envelope is:

```text
schema: echo.navigation.model.v1
checkpoint: original HypothesisAgent.checkpoint() result, unchanged
acquisition: completed acquisition episodes with physical receipts,
             matching calibration projections, metrics and journal references
```

Only completed episodes at the public goal with valid causal history can save.
The terminal physical movement can be learned by `finish`. A process request
that returns `done` may already have saved; its matching `finish` does not
learn or save again. A checkpoint hash verifies consistency, not the honesty
of a malicious local source of physical receipts.

## Sensor integration and collisions

Ray coordinates are world millimetres; they are **not rotated again by yaw**.
Cell centres are `(2000*x, 2000*z)` and faces are halfway between centres.
Sub-cell camera origins, including motor residual offsets, are accepted.
At most 65 rays and a maximum 12,000 mm Euclidean range are accepted. Endpoints
can lie outside the board; only in-bounds cells enter memory.

Exact grid crossings mark traversed cells free and a hit endpoint occupied.
At a grid face the hit belongs to the far side in the ray direction. Corner-only
touches do not clear neighboring cells; ambiguous travel along a face does not
clear cells. Occupied endpoints win over free segments in the same frame.
Later free measurements may revise remembered occupancy. Unobserved cells are
omitted from the `cells` array and never presented as measured walls. The current
cell has odometry evidence; a supplied goal has no automatic occupancy evidence.

A failed motor edge is stored independently of measured cells and motor effects.
If its destination is known, a blocked stationary receipt marks that destination
`inferred` and excludes it from routing. Unknown effects yield an edge-only
inference. The same failed edge is not immediately retried. A new free ray
measurement can refute an inferred obstacle; replaying identical ray geometry
does not erase the failure. Cancelled moves never become motor evidence or
measured occupied cells. Scanning changes the requested absolute camera yaw.

## Sessions, finish, limits and privacy

New sessions require sequence zero and the exact empty initial outcome. Use a
fresh GUID per episode. World, profile, dimensions and goal are immutable within
a session. Each subsequent decision request advances the sequence by one and
binds the preceding motor/scan receipt. Malformed requests fail before learning.
Only the **latest exact-byte** decision retry is cached; changed retries,
stale/gapped sequences, and unsolicited outcomes raise 409.

`finish` returns `echo.navigation.finish.v1`, with `status="closed"`, consequence
`resolution`, `matched`, `confirmed`, `pending`, `completed`, support, checkpoint
hash, work counters, metrics, journal path and `modelSaved`. It never proposes an
action or calls the planner:

* After `done` or `wait`, pass the **same sequence and exact observation bytes**.
  The old physical receipt is already consumed and is not processed again.
* After executing a move or scan, pass **next sequence** and its physical outcome.
* Pausing before a newly proposed action executes can send the same exact
  observation. Closure is `unresolved/pending`, with no invented execution.
* A cancelled action can close with next sequence and its actual cancelled
  receipt. Missing/unbound final receipts never create motor evidence.

Budgets are **320 decisions and 2,000,000 logical units per episode**. Work
includes checkpoint replay, sensor integration, calibration, projections,
frontier selection and actual core operations. Exhaustion yields `wait`;
`finish` still records closure at the limits. Logical units are not time/energy.
Journals report actual observations, actions, consequences and operation counts;
they are passive existing Recorder artifacts, not a second thought bus.

Up to **16 active sessions** are allowed. The **64 most recently closed** sessions
retain exact-byte finish replies; their large agent histories are released.
Older closed entries are evicted. A fixed-size conservative tombstone filter
rejects their session tokens with `409 retired_session` instead of restarting
them and replaying an action. Very rare filter false positives also reject a
new token: use a fresh GUID. Finish retries outside the retained cache return
404. Session retry state is in memory; after restarting the service, use fresh
GUIDs and never resend an old actuator proposal.

Journal/persistence failures fail the session closed. HTTP retains the existing
loopback peer/Host checks, browser Origin rejection, strict framing, 1 MiB body
limit, five-second read timeout, no-store responses and no file-serving endpoints.
Unknown JSON fields (including layouts/effects), duplicate keys, nonfinite values
and bools in integer fields are rejected. These guards are for a native local
client; they do not authenticate other programs running as the same user.

## CPU verification

```sh
cd Bridge  # from the echounity project root
python3 -B -u \
  -m unittest discover -s tests -p 'test_*.py' -v
```

`tests/test_navigation.py` uses hand-built development fixtures and multiple
private command permutations. It covers calibration/duplicate evidence,
checkpoint replay, strict JSON, geometry/occlusion, live obstruction detours,
frozen and cold profiles, no-memory behavior, world revisits, budgets, terminal
receipt handling, HTTP routes and fail-closed I/O. It does not run Unity, read
held-out maps, or certify the registered navigation success gate.

Verification on 2026-10-11: **93 tests passed** (35 navigation, 37 legacy bridge,
21 evaluator tests) in 11.073 seconds with the command above. Output:
`/tmp/echo-navigation-cli-green.log`. The CLI regression launches an isolated
server on an ephemeral port and checks a custom root, including a path with
spaces; it does not launch Unity or replace the server on port 8770.
