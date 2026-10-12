using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    public sealed class StationClient : MonoBehaviour
    {
        public static string NextMission="irrigation";
        public StationWorld World;
        public PredictiveMotor Motor;
        public bool Automatic {get; private set;}
        public bool Connected {get;private set;}
        public string Status="Conectando con el núcleo local…";
        public Decision Latest;
        public readonly List<string> Recent=new List<string>();
        public string Session {get;private set;}
        public bool ShowKnowledge=true;
        public int Sequence {get;private set;}
        public float Pace=.12f;
        LineRenderer planLine;
        bool pendingMovement,pendingManual,usedBridge;
        Vector2Int pendingFrom,pendingTarget;
        int pendingSeq;
        string pendingSession,pendingObjectId;
        void Start()
        {
            World.Mission=NextMission;
            Application.targetFrameRate=45;
            Session=Guid.NewGuid().ToString("N");
            var go=new GameObject("Plan observado");planLine=go.AddComponent<LineRenderer>();
            planLine.material=new Material(Shader.Find("Sprites/Default"));
            planLine.startColor=new Color(.3f,1,.83f,.8f);planLine.endColor=new Color(.3f,1,.83f,.2f);
            planLine.widthMultiplier=.045f;planLine.positionCount=0;
            StartCoroutine(Health());
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-stationCapture")>=0)StartCoroutine(CapturePanel());
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-stationCheck")>=0 && !FindFirstObjectByType<StationRunCheck>())
                new GameObject("Development integration receipt").AddComponent<StationRunCheck>();
        }
        IEnumerator Health()
        {
            using(var q=UnityWebRequest.Get("http://127.0.0.1:8770/health"))
            {
                q.timeout=4;yield return q.SendWebRequest();
                Connected=q.result==UnityWebRequest.Result.Success;
                Status=Connected?"ECHO-5 conectado · listo para iniciar":"Puente apagado · ejecuta Tools/start.sh";
            }
        }
        IEnumerator CapturePanel()
        {
            yield return new WaitForSecondsRealtime(3);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Application.dataPath,"../Logs"));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath,"../Logs/control-panel.png"));
        }
        public void Begin()
        {
            Pause();
            Session=Guid.NewGuid().ToString("N");Sequence=0;Latest=null;
            Automatic=true;usedBridge=true;Status="Observando el entorno";StartCoroutine(Loop());
        }
        public void Pause()
        {
            Automatic=false;StopAllCoroutines();FinalizeMovement("cancelled");Motor.Stop();
            FinishBridge();
            Status="Pausado · control manual disponible";
        }
        void BeginMovement(Vector2Int target,bool manual)
        {
            FinalizeMovement("cancelled");Motor.Stop();
            pendingMovement=true;pendingManual=manual;pendingFrom=World.Position;pendingTarget=target;
            pendingSession=manual?"manual":Session;pendingSeq=manual?World.Actions:Sequence;
            pendingObjectId=manual?"":(Latest?.objectId??"");
            if(!World.Blocked(target))Motor.SetTarget(StationWorld.ToWorld(target.x,target.y));
        }
        public void StartManual(Vector2Int target)
        {
            if(Automatic || World.Energy<=0 || !World.InBounds(target) || World.Blocked(target))return;
            BeginMovement(target,true);Status="Control manual · destino ("+target.x+", "+target.y+")";
        }
        void FinalizeMovement(string disposition)
        {
            if(!pendingMovement)return;
            bool arrived=Motor.Arrived && World.Position==pendingTarget && Vector3.Distance(Motor.transform.position,StationWorld.ToWorld(pendingTarget.x,pendingTarget.y))<=.2f;
            World.MovementResult(pendingFrom,pendingTarget,arrived,disposition);
            World.RecordOutcome(pendingSession,pendingSeq,"move",pendingObjectId,pendingTarget,arrived?"arrived":disposition);
            if(!pendingManual)Sequence=Mathf.Max(Sequence,pendingSeq+1);
            pendingMovement=false;Motor.Stop();Note(World.LastResult);
        }
        void FinishBridge()
        {
            if(!usedBridge || string.IsNullOrEmpty(Session))return;
            usedBridge=false;
            var observation=World.Observe(Session,Sequence);
            string payload=JsonUtility.ToJson(observation);
            var q=new UnityWebRequest("http://127.0.0.1:8770/finish","POST");
            q.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));q.downloadHandler=new DownloadHandlerBuffer();
            q.SetRequestHeader("Content-Type","application/json");q.timeout=5;
            // Independent of movement coroutines: pause/reload must not cancel its receipt.
            var operation=q.SendWebRequest();operation.completed+=_=>
            {
                if(q.result!=UnityWebRequest.Result.Success)Debug.LogWarning("ECHO terminal receipt not acknowledged: "+q.error);
                q.Dispose();
            };
        }
        void OnDisable()
        {
            if(World && World.Robot && Motor){FinalizeMovement("cancelled");Motor.Stop();FinishBridge();}
        }
        public void ResetMission(string mission)
        {
            Pause();NextMission=mission;SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        void Note(string text) {Recent.Insert(0,text);if(Recent.Count>8)Recent.RemoveAt(Recent.Count-1);}
        IEnumerator Loop()
        {
            try
            {
            while(Automatic && Sequence<400 && World.Energy>0)
            {
                var obs=World.Observe(Session,Sequence);
                string request=JsonUtility.ToJson(obs);
                using(var q=new UnityWebRequest("http://127.0.0.1:8770/decide","POST"))
                {
                    q.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(request));
                    q.downloadHandler=new DownloadHandlerBuffer();q.SetRequestHeader("Content-Type","application/json");q.timeout=10;
                    Status="ECHO evalúa observaciones y presupuesto…";yield return q.SendWebRequest();
                    if(!Automatic)yield break;
                    if(q.result!=UnityWebRequest.Result.Success)
                    {Connected=false;Status="Puente: "+q.error+" · "+q.downloadHandler.text;Automatic=false;Motor.Stop();yield break;}
                    Decision answer=null;
                    try {answer=JsonUtility.FromJson<Decision>(q.downloadHandler.text);}catch(Exception e){Status="Respuesta ilegible: "+e.Message;}
                    if(answer==null || answer.schema!="echo.station.decision.v1" || answer.session!=Session || answer.seq!=Sequence)
                    {Status="Respuesta desfasada o incompatible; movimiento detenido.";Automatic=false;Motor.Stop();yield break;}
                    Connected=true;Latest=answer;Status=answer.phase+" · "+answer.reason;
                    if(ShowKnowledge)World.ShowKnowledge(answer.knownCells);
                    DrawPlan(answer.plan);Note(answer.reason);
                    if(answer.action=="done") {Automatic=false;Status=World.Completed?"Misión cumplida y verificada por el entorno":"ECHO terminó sin confirmación de misión";yield break;}
                    if(answer.action=="move")
                    {
                        var from=World.Position;var to=new Vector2Int(answer.x,answer.z);
                        if(!World.InBounds(to) || Mathf.Abs(from.x-to.x)+Mathf.Abs(from.y-to.y)!=1)
                        {Status="Acción rechazada: destino no adyacente.";Automatic=false;yield break;}
                        BeginMovement(to,false);
                        if(!World.Blocked(to))
                        {
                            float deadline=Time.time+8;
                            while(Motor.HasTarget && !Motor.Arrived && Time.time<deadline)yield return null;
                        }
                        FinalizeMovement("blocked");
                    }
                    else if(answer.action=="inspect" || answer.action=="interact")
                    {
                        var o=World.FindObject(answer.objectId);
                        if(o) Motor.DesiredLookPoint=o.transform.position+Vector3.up*.8f;
                        bool applied=World.TryInteract(answer.objectId,answer.action=="inspect");
                        World.RecordOutcome(Session,Sequence,answer.action,answer.objectId,new Vector2Int(answer.x,answer.z),applied?"applied":"rejected");
                        Sequence++;
                        yield return new WaitForSeconds(.3f);
                    }
                    else if(answer.action=="wait")
                    {Automatic=false;Status="ECHO se abstiene: "+answer.reason;Motor.Stop();yield break;}
                    else {Automatic=false;Status="Acción desconocida; detenida.";Motor.Stop();yield break;}
                    Note(World.LastResult);yield return new WaitForSeconds(Pace);
                }
            }
            if(Sequence>=400)Status="Límite de decisiones alcanzado; sesión cerrada.";
            else if(World.Energy<=0)Status="Energía agotada; sesión cerrada.";
            }
            finally
            {
                // Every exit submits its last physical result before manual input can replace it.
                Automatic=false;FinalizeMovement("cancelled");Motor.Stop();FinishBridge();
            }
        }
        void DrawPlan(CellData[] plan)
        {
            if(!planLine)return;
            int n=plan==null?0:plan.Length;planLine.positionCount=n;
            for(int i=0;i<n;i++)planLine.SetPosition(i,StationWorld.ToWorld(plan[i].x,plan[i].z)+Vector3.up*.035f);
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Space)){if(Automatic)Pause();else Begin();}
            if(Input.GetKeyDown(KeyCode.V)) Motor.ShowPrediction=!Motor.ShowPrediction;
            if(Input.GetKeyDown(KeyCode.B)) World.SetDetour(!World.IsDetourBlocked);
            if(Automatic)return;
            if(pendingMovement && pendingManual && (!Motor.HasTarget || Motor.Arrived))FinalizeMovement("blocked");
            if(Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.I))
            {
                StationObject closest=null;float distance=10;
                foreach(var o in World.Objects)if(!o.Collected)
                {float d=Mathf.Abs(World.Position.x-o.X)+Mathf.Abs(World.Position.y-o.Z);if(d<distance){distance=d;closest=o;}}
                if(closest && distance<=1)
                {
                    FinalizeMovement("cancelled");
                    bool inspect=Input.GetKeyDown(KeyCode.I);int actionSeq=World.Actions;
                    bool applied=World.TryInteract(closest.Id,inspect);
                    World.RecordOutcome("manual",actionSeq,inspect?"inspect":"interact",closest.Id,new Vector2Int(closest.X,closest.Z),applied?"applied":"rejected");
                    Note(World.LastResult);
                }
            }
            if(Input.GetMouseButtonDown(0) && Camera.main && Camera.main.pixelRect.Contains(Input.mousePosition))
            {
                Ray ray=Camera.main.ScreenPointToRay(Input.mousePosition);
                if(Physics.Raycast(ray,out var hit,200,1<<9))
                {
                    var cell=new Vector2Int(Mathf.RoundToInt(hit.point.x/2),Mathf.RoundToInt(hit.point.z/2));
                    if(!World.Blocked(cell))StartManual(cell);
                }
            }
            if(!pendingMovement && !Motor.HasTarget)
            {
                Vector2Int delta=Vector2Int.zero;
                if(Input.GetKey(KeyCode.W))delta=Vector2Int.up;
                else if(Input.GetKey(KeyCode.S))delta=Vector2Int.down;
                else if(Input.GetKey(KeyCode.D))delta=Vector2Int.right;
                else if(Input.GetKey(KeyCode.A))delta=Vector2Int.left;
                if(delta!=Vector2Int.zero)
                {var p=World.Position+delta;if(!World.Blocked(p))StartManual(p);}
            }
        }
    }
}
