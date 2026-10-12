using System;
using System.IO;
using UnityEngine;

namespace EchoWorld
{
    public sealed class PrototypeGame : MonoBehaviour
    {
        public const string Version="Echo MMO Prototype BETA 0.0.2";
        public int Seed=72831;
        public ChunkWorld World {get;private set;}
        public PlayerExplorer Player {get;private set;}
        public Camera MainCamera {get;private set;}
        public LocalSession Session {get;private set;}
        public WorldHud Hud {get;private set;}
        public WorldDrops Drops {get;private set;}
        public ChestStore Chests {get;private set;}
        public TorchLighting Torches {get;private set;}
        public DayNightCycle DayNight {get;private set;}
        public string SaveRoot {get;private set;}
        float nextSave; bool ready;WorldSaveStore store;
        public static string Argument(string flag,string fallback=null)
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,flag);
            return i>=0&&i+1<args.Length?args[i+1]:fallback;
        }
        void Start()
        {
            if(int.TryParse(Argument("--world-seed"),out int selectedSeed))Seed=selectedSeed;
            Time.timeScale=1;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            QualitySettings.pixelLightCount=8;QualitySettings.shadowDistance=60;RenderSettings.fog=false;
            SaveRoot=Argument("--world-save-root",Path.Combine(Application.persistentDataPath,"worlds-beta-0.0.2","seed-"+Seed));
            try
            {
                store=new WorldSaveStore(SaveRoot);SaveRoot=store.Working;
                var saved=SessionPersistence.Read(SaveRoot,Seed);
                var data=new WorldData(Seed,SaveRoot);
                World=new GameObject("Earth chunk streaming").AddComponent<ChunkWorld>();World.Initialize(data);
                Session=new LocalSession();Chests=new ChestStore(this);
                var body=new GameObject("Local player");body.layer=2;Player=body.AddComponent<PlayerExplorer>();
                body.transform.position=new Vector3(.5f,data.StandHeight(0,0)+.05f,.5f);
                World.FollowTarget=body.transform;World.BuildImmediate();
                var cameraObject=new GameObject("Isometric camera");MainCamera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";
                var rig=cameraObject.AddComponent<IsometricCamera>();rig.Target=body.transform;rig.World=World;rig.SnapToTarget();
                Hud=gameObject.AddComponent<WorldHud>();Hud.Game=this;rig.Hud=Hud;
                Player.Game=this;Player.World=World;Player.View=MainCamera;Player.Hud=Hud;Player.Session=Session;
                Drops=gameObject.AddComponent<WorldDrops>();Drops.Game=this;
                Torches=gameObject.AddComponent<TorchLighting>();Torches.Game=this;
                var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;
                DayNight=gameObject.AddComponent<DayNightCycle>();DayNight.Sun=sun;DayNight.View=MainCamera;
                if(saved!=null)
                {
                    Session.Restore(saved.inventory,saved.health,saved.stamina);Chests.Restore(saved.chests);Drops.Restore(saved.drops);
                    DayNight.SetTime(saved.clock);Player.RestorePosition(saved.cell,saved.offset);Player.RestoreFall(saved.airborne,saved.fallApex,saved.vertical);
                }
                else DayNight.SetTime(225);
                ready=true;Save();nextSave=Time.unscaledTime+30;
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"--world-smoke")>=0)gameObject.AddComponent<PrototypeSmoke>();
                Debug.Log(Version+" ready; seed="+Seed+"; local world="+SaveRoot);
            }
            catch(Exception error){Debug.LogException(error);enabled=false;}
        }
        void Update()
        {if(ready&&Time.unscaledTime>=nextSave){Save();nextSave=Time.unscaledTime+30;}}
        public SessionRecord Snapshot()
        {
            Vector3Int cell=World.ToGlobal(Player.transform.position);
            return new SessionRecord{seed=Seed,inventory=Session.SnapshotInventory(),health=Session.Health,stamina=Session.Stamina,
                cell=cell,offset=Player.transform.position-World.ToLocal(cell),clock=DayNight.Elapsed,chests=Chests.Snapshot(),drops=Drops.Snapshot(),
                airborne=Player.Airborne,fallApex=Player.FallApex,vertical=Player.VerticalSpeed};
        }
        public bool Save()
        {
            if(!ready)return false;
            try{World.Data.SaveAll();SessionPersistence.Write(SaveRoot,Snapshot());store.Commit();return true;}
            catch(Exception error){if(Player)Player.Notice="No se pudo guardar: "+error.Message;Debug.LogException(error);return false;}
        }
        public void QuitGame()
        {
            if(!Save())return;Time.timeScale=1;
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
            #else
            Application.Quit();
            #endif
        }
        void OnApplicationQuit(){Save();}
    }
}
