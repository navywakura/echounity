# ECHO Station bridge

Stdlib Python sidecar for the native Unity station. It imports the installed
ECHO-5 `HypothesisAgent.plan`, `Budget`, and `Recorder`. No dependency installation,
LLM, hidden Unity map, replacement route planner, or second thought bus is used.
The frozen sources are read only; imports disable bytecode writes.

## Start

```sh
cd Bridge  # from the echounity project root
python3 -B server.py
```

The server binds **only `127.0.0.1:8770`**. `--port` can select a different
loopback port for development. `ECHO5_SOURCE` is an explicit source-root override:

```sh
ECHO5_SOURCE=/path/to/echoai/ECHO-5 python3 -B server.py
```

By default the bridge imports the sibling `../echoai/ECHO-5` checkout (relative
to the project root). `ECHOAI_SOURCE` can name an alternate complete echoai
checkout; `ECHO5_SOURCE` takes precedence. The source must retain the layout
required by `echo5.legacy`; no package or virtual environment is installed or copied.
An already imported, different ECHO-5 installation is rejected.

## HTTP contract

`GET /health` returns only:

```json
{"schema":"echo.station.health.v1","status":"ok"}
```

`POST /decide` and `POST /finish` require UTF-8 `application/json` (optional `charset=utf-8`),
one decimal `Content-Length`, and a body no larger than **1,048,576 bytes**.
Duplicate JSON keys, nonfinite numbers, malformed Unicode, unknown fields,
wrong types (including booleans where integers belong), duplicate cell positions
or object IDs, oversized fields, chunked transfer, and compressed bodies fail.
Connections close after each response; socket reads time out after five seconds.

Only loopback peers and matching `127.0.0.1:port` / `localhost:port` Host headers
are accepted. Browser Origin headers and cross-site requests are rejected.
There are no CORS headers, file-serving routes, or state/journal GET endpoints.
This protects a native local integration; it is not authentication against
other programs running as the same local user.

The request contains exactly these fields:

| Field | Value |
| --- | --- |
| `schema` | `echo.station.observation.v1` |
| `session` | Fresh random token, 1–128 ASCII letters/digits or `_.:-`; use a GUID |
| `seq` | Integer 0–2,147,483,647; Unity starts at 0 |
| `mission` | `irrigation`, `delivery`, or `detour` |
| `width`, `height` | Integers, 2–15 and 2–13; Unity uses 15×13 |
| `x`, `z` | Current in-bounds integer grid coordinates |
| `battery` | Integer 0–100, Unity's ceiling of physical energy |
| `inventory` | Up to three strings: `key`, `battery`, `parcel` |
| `cells` | Observed cells only, each exactly `{x,z,blocked}`; `blocked` is boolean |
| `objects` | Observed, uncollected objects only; fields below |
| `lastResult` | Public result string, at most 4096 characters |
| `completed` | Boolean, Unity's completion verdict |
| `outcome` | Required structured physical-action receipt, defined below |

The initial empty receipt has exactly these fields and defaults:

```json
{"present":false,"session":"","seq":-1,"action":"","objectId":"","status":"","targetX":0,"targetZ":0,"actualX":0,"actualZ":0,"distanceMm":0}
```

After an action, `present:true` identifies its proposal's `session`, `seq`,
`action`, `objectId`, and target cell (`targetX,targetZ`). For moves, preserve
the decision's `objectId` even when it names an associated mission object.
`actualX,actualZ` is Unity's current rounded grid position. All coordinates
are in bounds. Sequence is a nonnegative 32-bit integer; `distanceMm` is a
nonnegative 32-bit integer residual distance for moves, and zero for interactions.
Receipt session/ID strings use the identifier rules above (object ID may be empty).

Move statuses are `arrived`, `blocked`, `cancelled`, or `rejected`.
Inspect/interact statuses are `applied` or `rejected`. A move is confirmed only
when the receipt matches **every proposal binding field**, its actual position
matches the current observation and target, its status is `arrived`, and
`distanceMm <= 200`. Rounded position alone never proves physical arrival.
An interaction needs a matching `applied` receipt plus current inventory,
visible inspection/active/locked facts, or observed charge. Out-of-view remembered
flags alone cannot confirm an interaction.

A fresh session may carry a well-formed previous receipt with session `manual`.
It remains part of the public observation but is not credited to an action in
the new session. For an expected action, a missing or mismatched receipt records
an unresolved consequence and produces `wait` / `outcome_unresolved`, without
issuing another actuator action. Malformed receipt fields produce HTTP 400.

Every object has exactly `id,kind,label,x,z,inspected,compatible,active,locked`.
IDs follow the session token character/length rules; labels are at most 256
characters. The final four fields are booleans. Kinds are `key`, `battery`,
`door`, `pump`, `valve`, `charger`, `parcel`, `delivery`, `terminal`, and
`scenery` (added to match `StationWorld`). Lists are bounded by map area;
the session remembers at most that many distinct object IDs. Kind and position
must stay stable for an ID within a session. Compatibility is ignored until
inspection, even if an uninspected payload supplies `compatible:true`.

The flat response contains exactly:

```text
schema="echo.station.decision.v1", session, seq,
action="move"|"inspect"|"interact"|"wait"|"done",
x, z, objectId, reason, prediction,
hypotheses:string[], evidence:string[],
budgetUsed:int, budgetRemaining:int,
knownCells:{x:int,z:int,blocked:bool}[], plan:{x:int,z:int}[],
source:string, journal:string, phase:string
```

All arrays are non-null, including empty arrays. All strings are non-null;
unused `objectId` is `""`. `knownCells` contains only actually received cell
evidence, updated by later visible observations. It is remembered terrain,
not a guarantee that unseen terrain has stayed unchanged. For `move`, `x,z`
is exactly the next one-cell step of `plan`; `plan` omits the starting cell.
For inspect/interact, `x,z` is the object's position; Unity acts on `objectId`.
Wait/done uses the current position. Unity must return the next observation
after executing an action; the bridge never assumes physical arrival.

Error responses are `{ "error": "code" }`, not decision objects:

| Status | Meaning |
| --- | --- |
| 400 / 411 / 413 / 415 | Invalid payload/framing, missing length, oversized body, unsupported encoding/type |
| 403 | Nonlocal/native request policy violation |
| 404 / 405 / 408 | Unknown endpoint or finish session, unsupported method, read timeout |
| 409 | Changed retry/finish, closed session, stale/skipped sequence, changed scenario/object identity, object history cap |
| 429 | 400 unique accepted requests already used in this session |
| 503 | Session cap reached, or a partially failed session cannot safely resume |
| 500 | Internal or journal failure; no unrecorded decision is returned |

## Sessions and retries

The first sequence may be any valid nonnegative number. Subsequently it must
increase by exactly one. Only the **latest** request can be retried, using its
**exact original UTF-8 bytes**. It receives the cached response bytes, with
no extra budget charge, journal events, or action computation. Changing even
whitespace at the same sequence is rejected. Older sequences and gaps are
rejected. A retry at the request cap remains valid while the session is open.

A session binds its mission and dimensions and owns an independent
**1,000,000 logical-unit Budget**, request counter, observed memory, planner,
and journal. The limit is **400 accepted unique requests**. Default capacity
is **16 sessions per server lifetime**, with no silent expiry or eviction:
eviction could otherwise turn an old retry into a new action. Restart the
bridge after reaching that limit. A new Unity `Begin()` should generate a new
random session and sequence 0, including after pause/resume or manual changes.
A new mission reloads the world and starts a new session. Server restarts do
not restore session state; restart the Unity session too.

## Closing a session

On pause or scene reload, send `POST /finish` with the **same complete
observation schema**, including the most recent actual outcome. It accepts
observation `seq` equal to the latest server proposal's sequence or that sequence
plus one. This covers a pending request whose response was never executed, and
an action already finalized by Unity. The receipt itself must still match the
latest proposal before any consequence can be confirmed.

Finish records the terminal observation, any applicable last consequence, and
a `station_finish` closure. It never calls `_choose`, plans a route, or returns
an action. A cancelled matching receipt records a failed, unconfirmed consequence.
Missing or stale receipts mark the proposal unresolved and pending; they do not
prove nonexecution. A latest wait/done proposal has no actuator consequence.

The acknowledgement is a separate flat object:

```text
schema="echo.station.finish.v1", session, seq, status="closed",
resolution="confirmed"|"failed"|"unresolved"|"no_action",
matched:bool, confirmed:bool, journal:string
```

The exact finish request bytes can be retried for the exact cached response,
without another record or budget change. Changed finish bytes return 409.
All `/decide` calls for a closed session return 409, including old decision
retries, so a cached actuator proposal cannot escape closure. Closed session
IDs remain reserved within the server's session cap. Unknown sessions return
404 without creating a journal or forged receipt; this can occur when pausing
before the first decide request reaches the server.

Finish uses the same body limits, framing, JSON validation, Host, and Origin
guards as decide. It remains available after the decision request limit or
logical budget is exhausted; recording closure consumes no new planning budget.
Unity should finalize pending physical movement before sending the receipt and
send finish independently of the coroutine cancelled by pause/reload.

## Planning and declared rules

The adapter remembers only received observations and confirmed inventory
changes. Out-of-view objects remain remembered; omission alone never means
collection. A pickup action is confirmed by a matching applied receipt and its
inventory increment or a currently observed active flag. Public true `active`
and inspected evidence persist by ID. Visible
terrain always updates the remembered cell value, including after unlocking.

The adapter selects a mission target or an observed frontier, then calls
`HypothesisAgent.plan` for the route. There is no separate BFS/A* here. The
planner receives a temporary exclusion mask containing blocked, occupied, and
unknown cells. Unknowns are not stored, reported, or journaled as observed walls.
Route depth is bounded by map area. Coordinates convert `(x,z)` to ECHO `(z,x)`.
The calibrated `effect_prior=((0,1),(0,-1),(1,0),(-1,0))` is **declared public
motor knowledge**, never a learned transition receipt. The adapter does not
call `observe()` to fabricate motor training.

Interactions approach a known-free adjacent cell. `charger`, `delivery`, and
`key` also permit standing on the target, matching the updated Unity world.
Other object cells are excluded while occupied; open doors require observed
free terrain. `scenery` blocks routing. `terminal` has no invented actuator.

Mission preconditions are written symbolic rules, not autonomously learned:

- Irrigation/detour: obtain key; inspect batteries and collect a compatible
  one; unlock an observed door with the key; power the pump with the battery;
  open the valve after observed pump activation; return to the known charger.
  Unity retains the key and consumes the battery on pump activation.
- Delivery: collect parcel; interact with delivery terminal; return to charger.
- Charge below 25 preempts the mission whenever a known affordable route to
  the charger exists; charging continues until the observation reports 100,
  then mission work resumes. Declared costs match `StationWorld`: 0.28 per move
  and 0.12 per interaction. Feasibility conservatively accounts for the ceiling
  in the integer battery observation. No known affordable route yields wait.
- A full three-item inventory yields wait when another pickup is required;
  no drop action is invented. `completed:true` is the completion authority.

Reasons, predictions, hypotheses, and evidence are deterministic summaries
of these branches, public fields, and declared priors. They are not model prose
or an inner monologue. No mission geometry or object placement is hardcoded in
the production adapter; the exact map appears only in the test evaluator.

## Journals and observation

Each session creates a fresh, exclusive file:

```text
<project>/Artifacts/Unity/run-<UTC>-<random>/thought.jsonl
```

The response's `journal` names that local file. It uses the existing ECHO-5
`Recorder` and its inherited hash-chained ARC journal schema. Records contain
actual public observations, planner results attributed to the declared prior,
decisions, budgets, receipt matching, next-observation consequences, and session
closure. Consequence records distinguish `matched`, `confirmed`, and `resolution`
and retain the public `outcome`; unresolved records name missing/mismatched fields.
Hidden Unity state is
never passed to the adapter or journal. These files are passive artifacts;
viewer text is never fed back into the policy.

To open the existing mini-terminal viewer on a returned path:

```sh
python3 -B ../echoai/observe.py --launch --technical /absolute/path/from/journal
```

No sealed campaign or benchmark is reopened. Journal disk failures stop that
session instead of allowing a possibly repeated unrecorded action.

## Tests

Run with system Python; no virtual environment or dependencies are needed:

```sh
cd Bridge  # from the echounity project root
python3 -B -u -m unittest discover -s tests -p test_station_agent.py -v > /tmp/echo-station-tests.log 2>&1 &
```

The fail-first baseline had 22 failing tests before implementation. The current
suite has **37 passing tests**: real planner route/prior and budget evidence,
partial maps, unknown compatibility, changed obstacles, actuator failure,
recharging, exact-byte retries, stale/gapped sequences, independent sessions,
caps, strict JSON/HTTP validation, and two deterministic mission evaluators.
The outcome/finish regressions cover required/default receipt fields, foreign
manual receipts, each binding field, rounded midpoint false arrivals, the 200 mm
boundary, current interaction facts, cancelled closure without planning, both
accepted finish sequences, stale/missing receipts, exact-byte finish idempotency,
closed-session rejection, unknown-session rejection, and closure at budget limits.
Latest full run: 37 tests passed in 4.217 seconds; stdout/stderr log:
`/tmp/echo-station-outcomes-green-2.log`.

The exact `StationWorld` evaluator includes radius-five wall/door occlusion,
scenery, collected-object disappearance, retained keys, fractional energy,
completion within distance one of the charger, and the dynamic detour at
`(5,9)`, activated just after the first battery-carrying move from `(3,9)` to
`(4,9)`. Assertions check that every subsequent proposed path, requested step,
and observed arrival avoids `(5,9)`, and that the robot attempts and reaches an
alternate crossing at `(5,3)` or `(7,6)` before completing. Only public
observations and physical-action receipts reach production code. Six runs passed:

| Mission | Initial charge | Requests | Logical units |
| --- | ---: | ---: | ---: |
| irrigation | 100 | 83 | 73,531 |
| irrigation | 24 | 84 | 73,573 |
| delivery | 100 | 74 | 66,452 |
| delivery | 24 | 75 | 66,494 |
| detour | 100 | 97 | 89,250 |
| detour | 24 | 98 | 89,292 |

Each low-charge run recharged and resumed. Six additional radius-two fixture
runs passed in 108–117 requests with 79,759–101,382 units. These numbers verify
the sidecar and deterministic fixtures; actual Unity physics runs are separate.

## Limits

A newly observed obstruction causes replanning over updated public cells.
Otherwise, a matched receipt that fails to confirm the action permits one retry;
another such failure stops with `wait` / `actuator_failure`. A failed move whose
rounded position changed also stops, even if the rounded position equals the
target. Missing/mismatched receipts stop with `outcome_unresolved` instead of
guessing execution or retrying an unbound action. The adapter does not invent a
new actuator model, learn altered controls,
or route through hidden cells to escape a stall. Wait requires the operator to
resolve the condition and start a fresh session as appropriate.

Frontier selection is a declared heuristic. It tries reachable known-free
frontiers through the real planner and can spend budget on unsuccessful routes;
there is no completeness or optimality claim for arbitrary partial worlds.
Budget exhaustion returns wait. This bridge handles the supplied station
missions, not arbitrary movable objects, respawns, or world resets inside one
session. Local journals grow with accepted requests; capacity and request caps
bound one server run, but artifact retention between runs is the owner's choice.
