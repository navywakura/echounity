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
        // Evaluator hook to inject an actual obstacle after sensing, before the motor ticks.
        public event Action<Vector2Int> MovementPrepared;
        int pendingCommand;
        Vector2Int pendingFrom,pendingTarget;
        bool usedBridge;
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
            Pause();Session=Guid.NewGuid().ToString("N");Sequence=0;Latest=null;World.LastOutcome=new NavOutcome();usedBridge=true;
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
            Automatic=false;StopAllCoroutines();FinalizeMove(true);
            if(World && World.Motor)World.Motor.Stop();Finish();Status="Pausado · memoria y recibos conservados";
        }
        void Finish()
        {
            if(!usedBridge || !World || World.Case==null)return;usedBridge=false;
            var q=Request("/navigate/finish",World.Observe(Session,Sequence,Profile));
            q.SendWebRequest().completed+=_=>{if(q.result!=UnityWebRequest.Result.Success)Debug.LogWarning("Navigation terminal receipt not acknowledged: "+q.downloadHandler.text);q.Dispose();};
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
                while(Automatic && Sequence<320)
                {
                    using(var q=Request("/navigate",World.Observe(Session,Sequence,Profile)))
                    {
                        yield return q.SendWebRequest();if(!Automatic)yield break;
                        if(q.result!=UnityWebRequest.Result.Success){Status="Puente: "+q.downloadHandler.text;yield break;}
                        var d=JsonUtility.FromJson<NavDecision>(q.downloadHandler.text);
                        if(d==null || d.schema!="echo.navigation.decision.v1" || d.session!=Session || d.seq!=Sequence)
                        {Status="Respuesta incompatible; actuadores detenidos";yield break;}
                        Connected=true;Latest=d;Status=d.reason;DrawPlan(d.plan);
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
            finally {Automatic=false;FinalizeMove(true);if(World && World.Motor)World.Motor.Stop();Finish();}
        }
        void DrawPlan(NavPoint[] plan)
        {
            if(!line)return;line.positionCount=plan?.Length??0;
            for(int i=0;i<line.positionCount;i++)line.SetPosition(i,NavigationWorld.Point(plan[i].x,plan[i].z)+Vector3.up*.05f);
        }
        public void Load(string id,string profile)
        {
            Pause();NavigationWorld.NextCase=id;NavigationWorld.NextProfile=profile;
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
        void OnDisable(){if(World && World.Robot){Automatic=false;FinalizeMove(true);World.Motor.Stop();Finish();}}
        void OnDestroy(){if(line && line.sharedMaterial)Destroy(line.sharedMaterial);}
    }
}
