# Protocolo de navegación v1

POST /navigate y /navigate/finish en el servidor existente 127.0.0.1:8770.
POST /navigate decide una acción; finish registra solamente la consecuencia final.
Los endpoints antiguos permanecen intactos. Tipos JSON estrictos.

Observación (todos los campos obligatorios):
```
{schema:"echo.navigation.observation.v1", session:string, seq:int,
 world:string, profile:"learn"|"trained"|"cold"|"no_memory"|"remembered",
 width:int[5,16],height:int[5,16],x:int,z:int,
 goalX:int,goalZ:int, yaw:int[0,359], completed:bool,
 rays:[{sx:int,sz:int,ex:int,ez:int,hit:bool}],
 outcome:{present:bool,session:string,seq:int,action:string,command:int,
          status:string,fromX:int,fromZ:int,x:int,z:int,distanceMm:int}}
```
Rays sx/sz/ex/ez are millimetres in the global odometry frame. Cell centers are
(x*2000,z*2000). World bounds are half-cell faces. Hit means occupied endpoint;
empty ray segments are free measurements. No object id/type/seed/layout is sent.
At most 65 rays per observation, range <=12000mm. Navigation geometry collisions
use existing layer8, floor9, own robot excluded. Initial outcome present=false,
seq=-1,command=-1,strings empty,other integers zero. Outcome actions move/scan;
move command0..3, scan command=-1; statuses arrived/blocked/cancelled or scanned.
Move x/z is actual rounded pose, from is actual starting cell; residual distance
is to the actual actuator target, not a planned/predicted claim. Successful motion
requires status arrived and <=200mm. Requests are sequenced, exact retries cached.

Decision:
```
{schema:"echo.navigation.decision.v1",session:string,seq:int,
 action:"move"|"scan"|"done"|"wait",command:int,yaw:int,
 reason:string,prediction:string,evidence:string[],hypotheses:string[],
 cells:[{x:int,z:int,state:"free"|"occupied"|"inferred"}],
 plan:[{x:int,z:int}],support:int[4],learnedActions:int,
 budgetUsed:int,budgetRemaining:int,modelHash:string,journal:string}
```
Move carries only the command handle, not the true movement effect. Scan rotates
the camera to requested absolute yaw (0=+z,90=+x). The client then captures the
new camera frustum. Goal is user-specified navigation coordinate, not perception.
Explanations are concise factual Spanish summaries of the receipt/model.

Python NavigationService(artifact_root, model_path=None) exposes process(raw),
finish(raw),close(). NavigationAgent accepts only validated observation dicts,
a core HypothesisAgent (empty/restored), Budget and Recorder; no world generator.
Profile learn can persist model on completion; trained/no_memory read checkpoint
but do not update learned actuator model; cold learns per-session without saving.
remembered loads same-world map and trained actuator checkpoint; new world starts
empty. Persisted model is original core checkpoint plus declared acquisition
provenance, not weights made from evaluator truth. Support is obtained from
checkpoint model, not inserted by hand. Missing trained model fails explicitly.

C# DTO classes NavRay,NavOutcome,NavObservation,NavCell,NavPoint,NavDecision.
Sensor API RobotSensor.Initialize(Transform robot), SetYaw(float degrees),
CaptureDepth() -> NavRay[], property Image: RenderTexture, Yaw:int.
Camera plus depth share one frustum. CaptureDepth does not inspect StationWorld,
NavigationWorld, game object names, hidden map or object tags. RGB preview only;
policy perception currently depth + odometry. Sensor vertical row center, FOV
horizontal about100°, range10m,61 rays. Camera eye y=.92m relative to root,
nearClip .05. Public camera field View for review/testing. Avoid self hits.

Unity environment private mapping for this dataset: command0 west,1 north,
2 east,3 south. Agent must never receive this declaration or dataset assets.
Coordinates passed into HypothesisAgent are [row=z,column=x].
