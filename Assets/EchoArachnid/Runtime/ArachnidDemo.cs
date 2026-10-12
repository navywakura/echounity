using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace EchoStation.Arachnid
{
    [Serializable] public class NeuralFrame {public int tick;public int[] spikes,potential;}
    [Serializable] public class NeuralReceipt
    {
        public string schema,session,role,input_sha256;
        public int sequence,neurons,spikes_total,observer_neuron_updates;
        public bool drives_policy,homeostasis_available,cam_available;
        public double observer_seconds;
        public NeuralFrame[] frames;
        public NavBasalState basal;
    }
    [DefaultExecutionOrder(-1000)]
    public sealed class ArachnidDemo : MonoBehaviour
    {
        static bool initialized;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDemo(){initialized=false;}
        void Awake()
        {
            // Editor-side static assignments are reset on entering Play mode.
            // Set this demo's initial profile before NavigationWorld.Awake builds it.
            // Later scene changes (acquisition / another map) retain their choice.
            if(initialized)return;
            initialized=true;
            NavigationWorld.NextProfile="cold";
            NavigationWorld.NextCase=NavigationWorld.Cases().First(c=>c.split=="development").id;
        }
        public NavigationClient Client;
        public NeuralReceipt Neural {get;private set;}
        public string DiagnosticStatus="Esperando observación";
        public ArachnidGait Body {get;private set;}
        public Camera CloseCamera {get;private set;}
        public RenderTexture CloseImage {get;private set;}
        int lastRequested=-1;
        bool diagnosticPending;
        bool blockProbe,obstacleInjected,blockCaptured;
        float nextRequest;
        IEnumerator Start()
        {
            yield return null;
            Body=Client.World.Robot.GetComponentInChildren<ArachnidGait>();
            blockProbe=Environment.GetCommandLineArgs().Contains("-arachnidBlockProbe");
            Client.MovementPrepared+=ProbeObstacle;
            var go=new GameObject("Arachnid inspection camera — evaluator only");go.transform.SetParent(transform,false);
            CloseCamera=go.AddComponent<Camera>();CloseCamera.fieldOfView=35;CloseCamera.nearClipPlane=.02f;CloseCamera.farClipPlane=30;
            CloseCamera.backgroundColor=new Color(.065f,.09f,.12f);CloseCamera.clearFlags=CameraClearFlags.SolidColor;
            CloseImage=new RenderTexture(640,420,24){name="Arachnid body inspection"};CloseCamera.targetTexture=CloseImage;
            CloseCamera.depth=2;
            CloseCamera.enabled=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
            float until=Time.realtimeSinceStartup+12;
            while(!Client.Connected && Time.realtimeSinceStartup<until)yield return null;
            if(Environment.GetCommandLineArgs().Contains("-closureConditioning"))
            {
                if(!FindFirstObjectByType<ArachnidConditioningCheck>())
                    new GameObject("Controlled CAM encounter assay").AddComponent<ArachnidConditioningCheck>();
            }
            else if(Environment.GetCommandLineArgs().Contains("-closureCheck"))
            {
                if(!FindFirstObjectByType<ClosureRunCheck>())
                    new GameObject("ECHO Station closure check").AddComponent<ClosureRunCheck>();
            }
            else if(Environment.GetCommandLineArgs().Contains("-arachnidRecoveryCheck"))
            {
                if(!FindFirstObjectByType<ArachnidRecoveryCheck>())
                    new GameObject("Arachnid recovery development check").AddComponent<ArachnidRecoveryCheck>();
            }
            else
            {
                if(Client.Connected)Client.Begin();
                StartCoroutine(Capture());
            }
        }
        void ProbeObstacle(Vector2Int target)
        {
            // Development-only physical intervention after the proposal, before motion.
            // No fabricated receipt or privileged information enters the Python agent.
            if(!blockProbe || obstacleInjected || Client.Latest==null || Client.Latest.learnedActions<4)return;
            obstacleInjected=Client.World.Case.split=="development" && Client.World.SetObstacle(target,true,preserveGoalAccess:true);
            if(obstacleInjected)Debug.Log($"ARACHNID_PHYSICAL_PROBE session={Client.Session} seq={Client.Sequence} obstacle={target}");
        }
        void LateUpdate()
        {
            if(!Client || !Client.World || !Client.World.Robot)return;
            if(CloseCamera)
            {
                var robot=Client.World.Robot;
                CloseCamera.transform.position=robot.position+robot.TransformDirection(new Vector3(1.25f,1.05f,1.5f));
                CloseCamera.transform.LookAt(robot.position+Vector3.up*.22f);
            }
            if(Body)
            {
                var state=Client.Latest?.basal;
                // Presentation of measured E[6]; never used as an input to the actor.
                Body.SensorColor=state?.E!=null && state.E.Length==6?
                    Color.Lerp(new Color(0,.898f,1),new Color(1,.25f,.07f),Mathf.Clamp01((state.E[4]-10)/70f)):
                    new Color(.4f,.4f,.4f);
            }
            if(Client.Latest!=null && Client.Latest.seq!=lastRequested && !diagnosticPending && Time.realtimeSinceStartup>=nextRequest)
                StartCoroutine(FetchDiagnostic());
            if(!blockCaptured && Client.Latest?.basal?.E!=null && Client.Latest.basal.E[4]>=60)
            {
                blockCaptured=true;Directory.CreateDirectory("Logs/Arachnid");
                ScreenCapture.CaptureScreenshot("Logs/Arachnid/unity-basal-block.png");
            }
        }
        IEnumerator FetchDiagnostic()
        {
            diagnosticPending=true;string token=Client.Session;int sequence=Client.Latest.seq;
            using(var q=UnityWebRequest.Get(Client.Endpoint+"/diagnostic?session="+UnityWebRequest.EscapeURL(token)))
            {
                q.timeout=3;yield return q.SendWebRequest();
                if(q.result==UnityWebRequest.Result.Success)
                {
                    var receipt=JsonUtility.FromJson<NeuralReceipt>(q.downloadHandler.text);
                    if(receipt!=null && receipt.schema=="echo5.neural-diagnostic.v1" && receipt.session==token && !receipt.drives_policy)
                    {Neural=receipt;lastRequested=receipt.sequence;DiagnosticStatus="Layer256 · lectura de la observación";}
                    else DiagnosticStatus="Recibo de diagnóstico no válido";
                }
                else DiagnosticStatus="Diagnóstico no disponible";
            }
            nextRequest=Time.realtimeSinceStartup+.5f;diagnosticPending=false;
        }
        IEnumerator Capture()
        {
            yield return new WaitForSecondsRealtime(18);
            Directory.CreateDirectory("Logs/Arachnid");
            ScreenCapture.CaptureScreenshot("Logs/Arachnid/unity-basal-live.png");
            var previous=RenderTexture.active;var texture=new Texture2D(640,420,TextureFormat.RGB24,false);
            try {RenderTexture.active=CloseImage;texture.ReadPixels(new Rect(0,0,640,420),0,0);texture.Apply();File.WriteAllBytes("Logs/Arachnid/unity-body.png",texture.EncodeToPNG());}
            finally {RenderTexture.active=previous;Destroy(texture);}
            float deadline=Time.realtimeSinceStartup+180;
            while(!Client.TerminalAcknowledged && Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(1);
            var report=new DemoReport {connected=Client.Connected,completed=Client.TerminalCompleted,acknowledged=Client.TerminalAcknowledged,
                decisions=Client.Sequence,moves=Client.World.Moves,blocks=Client.World.Blocks,gaitReady=Body && Body.Ready,
                diagnosticPresent=Neural!=null,observerDrivesPolicy=Neural!=null && Neural.drives_policy,
                finitePose=Body && Body.FinitePose,poseFrames=Body?Body.PoseFrames:0,
                steppingFrames=Body?Body.SteppingFrames:0,minimumPlantedFeet=Body?Body.MinimumPlantedFeet:0,
                journal=Client.Latest?.journal,session=Client.Session,profile=Client.Profile,basal=Client.Latest?.basal,
                obstacleInjected=obstacleInjected,blockProbe=blockProbe};
            File.WriteAllText("Logs/Arachnid/unity-basal-result.json",JsonUtility.ToJson(report,true));
        }
        [Serializable] class DemoReport {public bool connected,completed,acknowledged,gaitReady,diagnosticPresent,observerDrivesPolicy,finitePose,blockProbe,obstacleInjected;public int decisions,moves,blocks,poseFrames,steppingFrames,minimumPlantedFeet;public string journal,session,profile;public NavBasalState basal;}
        void OnDestroy(){if(Client)Client.MovementPrepared-=ProbeObstacle;if(CloseCamera)Destroy(CloseCamera.gameObject);if(CloseImage){CloseImage.Release();Destroy(CloseImage);}}
    }
}
