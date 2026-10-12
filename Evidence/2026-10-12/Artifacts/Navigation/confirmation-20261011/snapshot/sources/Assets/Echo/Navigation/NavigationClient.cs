using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    public sealed class NavigationClient : MonoBehaviour
    {
        public NavigationWorld World;
        public bool Automatic {get;private set;}
        public bool Connected {get;private set;}
        public string Status="Conectando con ECHO…";
        public string Session {get;private set;}
        public int Sequence {get;private set;}
        [NonSerialized] public NavDecision Latest;
        public string Profile=>NavigationWorld.NextProfile;
        public bool Pending {get;private set;}
        public bool TerminalAcknowledged {get;private set;}
        public bool TerminalCompleted {get;private set;}
        public bool TerminalPending {get;private set;}
        public bool ModelSaved {get;private set;}
        public string TerminalError {get;private set;}="";
        // Evaluator hook to inject an actual obstacle after sensing, before the motor ticks.
        public event Action<Vector2Int> MovementPrepared;
        int pendingCommand;
        Vector2Int pendingFrom,pendingTarget;
        bool usedBridge,sessionOpen,stopRequested;
        NavObservation lastSent;
        [Serializable] class CloseReceipt {public string schema,status,session;public int seq=-1;public bool completed,pending=true,modelSaved;}
        LineRenderer line;
        void Start()
        {
            Session=Guid.NewGuid().ToString("N");Application.targetFrameRate=40;
            var go=new GameObject("Plan from acquired model");go.transform.SetParent(transform,false);
            line=go.AddComponent<LineRenderer>();line.material=new Material(Shader.Find("Sprites/Default"));
            line.startColor=new Color(.2f,.95f,.75f,.9f);line.endColor=new Color(.2f,.95f,.75f,.3f);line.widthMultiplier=.07f;
            line.positionCount=0;StartCoroutine(Health());
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-navigationCapture")>=0)StartCoroutine(Capture());
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-navigationCheck")>=0 && !FindFirstObjectByType<NavigationRunCheck>())
                new GameObject("Navigation physics verification").AddComponent<NavigationRunCheck>();
        }
        IEnumerator Health()
        {
            using(var q=UnityWebRequest.Get("http://127.0.0.1:8770/health"))
            {q.timeout=4;yield return q.SendWebRequest();Connected=q.result==UnityWebRequest.Result.Success;
             Status=Connected?"Listo · cámara y aprendizaje local":"Puente apagado · ejecuta Tools/start.sh";}
        }
        IEnumerator Capture()
        {
            yield return new WaitForSecondsRealtime(4);Directory.CreateDirectory("Logs");
            ScreenCapture.CaptureScreenshot("Logs/navigation-panel.png");
            var source=World.Sensor.Image;var previous=RenderTexture.active;
            var texture=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=source;texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);texture.Apply();
                File.WriteAllBytes("Logs/robot-camera.png",texture.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;Destroy(texture);}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-navigationPreview")>=0)
            {
                yield return new WaitForSecondsRealtime(2);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit(0);
#endif
            }
        }
        public void Begin()
        {
            if(sessionOpen){Automatic=true;Status="Continuando la misma sesión y su memoria";return;}
            Session=Guid.NewGuid().ToString("N");Sequence=0;Latest=null;World.LastOutcome=new NavOutcome();usedBridge=true;sessionOpen=true;stopRequested=false;
            lastSent=null;TerminalAcknowledged=false;TerminalCompleted=false;TerminalPending=true;ModelSaved=false;TerminalError="";
            Automatic=true;Status="Observando a través de la cámara de profundidad";StartCoroutine(Loop());
        }
        void FinalizeMove(bool cancelled)
        {
            if(!Pending)return;
            World.Record(Session,Sequence,"move",pendingCommand,pendingFrom,pendingTarget,cancelled);
            World.Sensor.SetYaw(World.Robot.eulerAngles.y);Sequence++;Pending=false;
        }
        public void Pause()
        {
            Automatic=false;
            Status=Pending?"Pausa solicitada · termina el movimiento actual":"Pausado · continúa esta sesión al iniciar";
        }
        public void StopSession()
        {
            stopRequested=true;Automatic=false;StopAllCoroutines();FinalizeMove(true);
            if(World && World.Motor)World.Motor.Stop();Finish();sessionOpen=false;
        }
        void Finish()
        {
            if(!usedBridge || !World || World.Case==null)return;usedBridge=false;
            string closingSession=Session;int closingSeq=Sequence;
            // No new action: close the exact request, even if the world changed meanwhile.
            var observation=lastSent!=null && lastSent.seq==Sequence?lastSent:World.Observe(Session,Sequence,Profile);
            SendClose(observation,closingSession,closingSeq,0);
        }
        void SendClose(NavObservation observation,string closingSession,int closingSeq,int attempt)
        {
            var q=Request("/navigate/finish",observation);
            q.SendWebRequest().completed+=_=>
            {
                try
                {
                    var receipt=q.result==UnityWebRequest.Result.Success?JsonUtility.FromJson<CloseReceipt>(q.downloadHandler.text):null;
                    bool ok=receipt!=null && receipt.schema=="echo.navigation.finish.v1" && receipt.status=="closed" && receipt.session==closingSession && receipt.seq==closingSeq;
                    if(Session==closingSession)
                    {TerminalAcknowledged=ok;TerminalError=ok?"":q.downloadHandler.text;
                     if(ok){TerminalCompleted=receipt.completed;TerminalPending=receipt.pending;ModelSaved=receipt.modelSaved;}}
                    if(!ok && attempt<2)SendClose(observation,closingSession,closingSeq,attempt+1);
                    else if(!ok)Debug.LogWarning("Navigation terminal receipt not acknowledged after retries: "+q.downloadHandler.text);
                }
                finally{q.Dispose();}
            };
        }
        UnityWebRequest Request(string route,NavObservation observation)
        {
            var q=new UnityWebRequest("http://127.0.0.1:8770"+route,"POST");
            q.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(observation)));
            q.downloadHandler=new DownloadHandlerBuffer();q.SetRequestHeader("Content-Type","application/json");q.timeout=15;return q;
        }
        IEnumerator Loop()
        {
            try
            {
                while(!stopRequested && Sequence<320)
                {
                    while(!Automatic && !stopRequested)yield return null;
                    if(stopRequested)yield break;
                    lastSent=World.Observe(Session,Sequence,Profile);
                    using(var q=Request("/navigate",lastSent))
                    {
                        yield return q.SendWebRequest();if(stopRequested)yield break;
                        if(q.result!=UnityWebRequest.Result.Success){Status="Puente: "+q.downloadHandler.text;yield break;}
                        var d=JsonUtility.FromJson<NavDecision>(q.downloadHandler.text);
                        if(d==null || d.schema!="echo.navigation.decision.v1" || d.session!=Session || d.seq!=Sequence ||
                           d.yaw<0 || d.yaw>359 || d.learnedActions<0 || d.learnedActions>4 || d.support==null || d.support.Length!=4 ||
                           d.cells==null || d.plan==null || d.hypotheses==null || d.evidence==null || d.budgetUsed<0 || d.budgetRemaining<0 ||
                           (long)d.budgetUsed+d.budgetRemaining!=2000000 || string.IsNullOrEmpty(d.journal) || string.IsNullOrEmpty(d.modelHash))
                        {Status="Respuesta incompatible; actuadores detenidos";yield break;}
                        Connected=true;Latest=d;Status=d.reason;DrawPlan(d.plan);
                        while(!Automatic && !stopRequested)yield return null;
                        if(stopRequested)yield break;
                        if(d.action=="done"){Status=World.Completed?"Meta alcanzada y confirmada por el entorno":"El agente terminó sin llegar a la meta";yield break;}
                        if(d.action=="wait")yield break;
                        if(d.action=="scan")
                        {
                            if(d.yaw<0 || d.yaw>359){Status="Orientación de cámara inválida";yield break;}
                            World.Sensor.SetYaw(d.yaw);World.Motor.DesiredLookPoint=World.Robot.position+Quaternion.Euler(0,d.yaw,0)*Vector3.forward*3+Vector3.up;
                            var p=World.Position;World.Record(Session,Sequence,"scan",-1,p,p,false);Sequence++;
                            yield return new WaitForSeconds(.12f);
                        }
                        else if(d.action=="move")
                        {
                            if(d.command<0 || d.command>3){Status="Comando de actuador inválido";yield break;}
                            pendingFrom=World.Position;pendingCommand=d.command;pendingTarget=World.Execute(d.command);Pending=true;
                            MovementPrepared?.Invoke(pendingTarget);
                            float deadline=Time.time+10;
                            while(World.Motor.HasTarget && !World.Motor.Arrived && Time.time<deadline)yield return null;
                            FinalizeMove(false);
                        }
                        else {Status="Acción desconocida; detenida";yield break;}
                        yield return new WaitForSeconds(.06f);
                    }
                }
                Status="Presupuesto de 320 decisiones agotado";
            }
            finally {Automatic=false;FinalizeMove(true);if(World && World.Motor)World.Motor.Stop();Finish();sessionOpen=false;}
        }
        void DrawPlan(NavPoint[] plan)
        {
            if(!line)return;line.positionCount=plan?.Length??0;
            for(int i=0;i<line.positionCount;i++)line.SetPosition(i,NavigationWorld.Point(plan[i].x,plan[i].z)+Vector3.up*.05f);
        }
        public void Load(string id,string profile)
        {
            StopSession();NavigationWorld.NextCase=id;NavigationWorld.NextProfile=profile;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        public void NextMap()
        {
            var cases=NavigationWorld.Cases().Where(c=>c.split=="development").ToArray();
            int i=Array.FindIndex(cases,c=>c.id==World.Case.id);Load(cases[(i+1)%cases.Length].id,"trained");
        }
        public void Train()=>Load(NavigationWorld.Cases().First(c=>c.split=="acquisition").id,"learn");
        public void PlaceObstacle()
        {
            if(Latest?.plan==null)return;
            foreach(var p in Latest.plan.Skip(1).Take(5))if(World.SetObstacle(new Vector2Int(p.x,p.z),true)){Status="El usuario ha añadido un obstáculo físico";return;}
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Space)){if(Automatic)Pause();else Begin();}
            if(Input.GetKeyDown(KeyCode.V))World.Motor.ShowPrediction=!World.Motor.ShowPrediction;
            if(Input.GetKeyDown(KeyCode.B))PlaceObstacle();
            if(!Automatic && Input.GetKeyDown(KeyCode.Q))World.Sensor.SetYaw(World.Sensor.Yaw-90);
            if(!Automatic && Input.GetKeyDown(KeyCode.E))World.Sensor.SetYaw(World.Sensor.Yaw+90);
        }
        void OnDisable(){if(World && World.Robot)StopSession();}
        void OnDestroy(){if(line && line.sharedMaterial)Destroy(line.sharedMaterial);}
    }
}
