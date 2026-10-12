using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EchoStation
{
    [Serializable] public class NavWorldCase
    {
        public string id,split,family;
        public int width,height;
        public NavPoint start,goal;
        public NavPoint[] walls,mapping;
    }
    [Serializable] public class NavWorldSet {public string schema;public NavWorldCase[] cases;}

    // Evaluator/environment. Its map and command mapping NEVER enter NavObservation.
    public sealed class NavigationWorld : MonoBehaviour
    {
        public static string NextCase="";
        public static string NextProfile="trained";
        public NavWorldCase Case {get;private set;}
        public Transform Robot {get;private set;}
        public PredictiveMotor Motor {get;private set;}
        public RobotSensor Sensor {get;private set;}
        public int Moves {get;private set;}
        public int Scans {get;private set;}
        public int Blocks {get;private set;}
        public NavOutcome LastOutcome=new NavOutcome();
        public Vector2Int Position => Robot ? new Vector2Int(Mathf.RoundToInt(Robot.position.x/2),Mathf.RoundToInt(Robot.position.z/2)) : Vector2Int.zero;
        public bool Completed => Case!=null && Robot && Vector3.Distance(Robot.position,Point(Case.goal.x,Case.goal.z))<=.2f;
        readonly Dictionary<Vector2Int,GameObject> occupied=new Dictionary<Vector2Int,GameObject>();
        readonly List<Material> materials=new List<Material>();
        GameObject generated;
        Material wall,trim,orange;
        public static Vector3 Point(int x,int z)=>new Vector3(x*2,0,z*2);
        public static NavWorldCase[] Cases()
        {
            string path=Path.Combine(Application.streamingAssetsPath,"Navigation/worlds.json");
            var data=JsonUtility.FromJson<NavWorldSet>(File.ReadAllText(path));
            if(data.schema!="echo.navigation.worlds.v1" || data.cases==null || data.cases.Length==0)throw new InvalidDataException("Invalid navigation dataset");
            return data.cases;
        }
        void Awake()
        {
            if(!Application.isPlaying)return;
            var cases=Cases();
            Build(cases.FirstOrDefault(c=>c.id==NextCase) ?? cases.First(c=>c.split=="development"));
        }
        Material Material(string name,Color color,bool emission=false)
        {
            var m=new Material(Shader.Find("Standard")){name=name,color=color};m.SetFloat("_Metallic",.25f);m.SetFloat("_Glossiness",.25f);
            if(emission){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*.7f);}
            materials.Add(m);return m;
        }
        GameObject Cube(string name,Vector3 position,Vector3 scale,Material mat,int layer,bool solid)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(generated.transform,false);
            go.transform.position=position;go.transform.localScale=scale;go.layer=layer;go.GetComponent<Renderer>().sharedMaterial=mat;
            if(!solid){go.GetComponent<Collider>().enabled=false;DestroyObject(go.GetComponent<Collider>());}
            return go;
        }
        static void DestroyObject(UnityEngine.Object o){if(!o)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
        void Cleanup()
        {
            if(generated){generated.SetActive(false);DestroyObject(generated);}
            foreach(var m in materials)DestroyObject(m);materials.Clear();occupied.Clear();
        }
        public void Build(NavWorldCase c)
        {
            if(c==null || c.width<5 || c.width>16 || c.height<5 || c.height>16 || c.mapping?.Length!=4)throw new ArgumentException("Invalid navigation case");
            Cleanup();Case=c;Moves=Scans=Blocks=0;LastOutcome=new NavOutcome();
            generated=new GameObject("World geometry — private evaluator");generated.transform.SetParent(transform,false);
            var floor=Material("Navigation floor",new Color(.13f,.21f,.27f));
            wall=Material("Navigation bulkhead",new Color(.22f,.32f,.40f));
            trim=Material("Navigation strip",new Color(.19f,.84f,.78f),true);
            orange=Material("Changed obstruction",new Color(.96f,.5f,.12f));
            var hull=Material("Navigation hull",new Color(.035f,.055f,.085f));
            Cube("Deck hull",new Vector3(c.width-1,-.38f,c.height-1),new Vector3(c.width*2+.3f,.5f,c.height*2+.3f),hull,0,false);
            for(int z=0;z<c.height;z++)for(int x=0;x<c.width;x++)
                Cube("Deck",new Vector3(x*2,-.12f,z*2),new Vector3(1.975f,.24f,1.975f),floor,9,true);
            foreach(var p in c.walls)AddWall(new Vector2Int(p.x,p.z),false);
            var green=Material("User waypoint",new Color(.21f,.96f,.56f),true);
            Cube("User waypoint — not a sensor label",Point(c.goal.x,c.goal.z)+Vector3.up*.03f,new Vector3(1.4f,.06f,1.4f),green,0,false);
            for(int i=0;i<4;i++)
            {float sign=i%2==0?1:-1;var offset=i<2?new Vector3(sign*.65f,.45f,0):new Vector3(0,.45f,sign*.65f);
             Cube("Waypoint post",Point(c.goal.x,c.goal.z)+offset,new Vector3(.09f,.9f,.09f),green,0,false);}
            var robot=new GameObject("ECHO — learning robot");robot.transform.SetParent(generated.transform,false);Robot=robot.transform;
            Robot.position=Point(c.start.x,c.start.z);Motor=robot.AddComponent<PredictiveMotor>();RobotVisual.Build(Robot);
            Sensor=robot.AddComponent<RobotSensor>();Sensor.Initialize(Robot);Sensor.SetYaw(0);
            if(Camera.main)
            {
                var iso=Camera.main.GetComponent<IsoCamera>();
                if(iso){iso.Focus=new Vector3(c.width-1,0,c.height-1);iso.Follow=Robot;iso.Tracking=false;}
                Camera.main.orthographicSize=Mathf.Max(c.width,c.height)*1.12f;
            }
            Physics.SyncTransforms();
        }
        void AddWall(Vector2Int p,bool changed)
        {
            if(occupied.ContainsKey(p))return;
            var block=Cube(changed?"Changed physical obstacle":"Bulkhead",Point(p.x,p.y)+Vector3.up*.675f,new Vector3(1.92f,1.35f,1.92f),changed?orange:wall,8,true);
            occupied[p]=block;
            if(!changed)Cube("Wall light",Point(p.x,p.y)+Vector3.up*1.37f,new Vector3(1.8f,.035f,.05f),trim,0,false);
        }
        public bool SetObstacle(Vector2Int p,bool present)
        {
            if(p==Position || p==new Vector2Int(Case.goal.x,Case.goal.z) || p.x<=0 || p.y<=0 || p.x>=Case.width-1 || p.y>=Case.height-1)return false;
            if(present)AddWall(p,true);
            else if(occupied.TryGetValue(p,out var go)){go.SetActive(false);DestroyObject(go);occupied.Remove(p);}
            Physics.SyncTransforms();return true;
        }
        public void Place(Vector2Int p)
        {
            Motor.Stop();var cc=Robot.GetComponent<CharacterController>();cc.enabled=false;Robot.position=Point(p.x,p.y);cc.enabled=true;Physics.SyncTransforms();
        }
        public Vector2Int Execute(int command)
        {
            if(command<0 || command>3)throw new ArgumentOutOfRangeException(nameof(command));
            var delta=Case.mapping[command];var target=Position+new Vector2Int(delta.x,delta.z);
            // No hidden-map precheck: the physical controller must actually attempt it.
            Motor.Stop();Motor.SetTarget(Point(target.x,target.y));return target;
        }
        public void Record(string session,int seq,string action,int command,Vector2Int before,Vector2Int target,bool cancelled)
        {
            int residual=action=="move"?Mathf.CeilToInt(Vector3.Distance(Robot.position,Point(target.x,target.y))*1000):0;
            bool arrived=action=="move" && Motor.Arrived && residual<=200 && Position==target;
            string status=action=="scan"?"scanned":arrived?"arrived":cancelled?"cancelled":"blocked";
            if(action=="move"){Moves++;if(status=="blocked")Blocks++;}else Scans++;
            LastOutcome=new NavOutcome {present=true,session=session,seq=seq,action=action,command=command,status=status,
                fromX=before.x,fromZ=before.y,x=Position.x,z=Position.y,distanceMm=residual};
            Motor.Stop();
        }
        public NavObservation Observe(string session,int seq,string profile)
        {
            return new NavObservation {session=session,seq=seq,world=Case.id,profile=profile,
                width=Case.width,height=Case.height,x=Position.x,z=Position.y,goalX=Case.goal.x,goalZ=Case.goal.z,
                yaw=Sensor.Yaw,rays=Sensor.CaptureDepth(),completed=Completed,outcome=LastOutcome};
        }
        void OnDestroy(){Cleanup();}
    }
}
