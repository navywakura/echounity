using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoStation.Editor
{
    public static class StationEditor
    {
        public const string ScenePath="Assets/Scenes/EchoStation.unity";
        const string Art="Assets/Art/AdventureForge/Props/";
        static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        static Transform root;
        static StationWorld world;
        static Material Mat(string name,Color color,bool emissive=false)
        {
            if(materials.TryGetValue(name,out var found) && found)return found;
            string path="Assets/Echo/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}
            m.color=color;m.SetFloat("_Metallic",.25f);m.SetFloat("_Glossiness",.35f);
            if(emissive){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*.8f);}
            materials[name]=m;return m;
        }
        static GameObject Cube(string name,Vector3 pos,Vector3 scale,Material mat,int layer=0,bool collision=true)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root);go.transform.position=pos;go.transform.localScale=scale;
            go.layer=layer;go.GetComponent<Renderer>().sharedMaterial=mat;
            if(!collision)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static void Label(string label,Vector3 point,Color color,float size=.085f)
        {
            var go=new GameObject(label);go.transform.SetParent(root);go.transform.position=point;
            go.transform.rotation=Quaternion.Euler(35.26439f,-45,0);
            var text=go.AddComponent<TextMesh>();text.text=label;text.fontSize=70;text.characterSize=size;text.anchor=TextAnchor.MiddleCenter;text.color=color;
            go.AddComponent<CameraFacing>();
        }
        static Sprite Sprite(string name)
        {
            string path=Art+name+".png";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new FileNotFoundException(path);
            if(importer.textureType!=TextureImporterType.Sprite || importer.spritePixelsPerUnit!=256)
            {
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=256;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
                importer.maxTextureSize=1024;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static StationObject Prop(string id,string kind,string label,int x,int z,string asset,float height=2.3f)
        {
            var obj=new GameObject(label);obj.transform.SetParent(root);obj.transform.position=StationWorld.ToWorld(x,z);
            obj.layer=8;var o=obj.AddComponent<StationObject>();o.Id=id;o.Kind=kind;o.Label=label;o.X=x;o.Z=z;
            var vis=new GameObject("Pack / "+asset);vis.transform.SetParent(obj.transform,false);
            vis.transform.localPosition=new Vector3(0,height*.38f,0);vis.transform.rotation=Quaternion.Euler(35.26439f,-45,0);
            var sr=vis.AddComponent<SpriteRenderer>();sr.sprite=Sprite(asset);sr.sortingOrder=0;
            vis.transform.localScale=Vector3.one*(height/sr.sprite.bounds.size.y);
            vis.AddComponent<CameraFacing>();o.Visual=vis;
            if(kind!="charger" && kind!="delivery" && kind!="key")
            {var c=obj.AddComponent<BoxCollider>();c.center=new Vector3(0,.7f,0);c.size=new Vector3(.95f,1.4f,.95f);}
            world.Objects.Add(o);return o;
        }
        static void Decorative(string asset,int x,int z,float height=2.3f)
        {
            var o=Prop("decor-"+x+"-"+z,"scenery",asset,x,z,asset,height);
            // Decoration is physically occupied and sensed as blocked; no goal semantics.
            o.Label="Equipo de la estación";
        }
        static void PersistGeneratedAssets()
        {
            int n=0;
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach(var m in renderer.sharedMaterials)
                    if(m && !AssetDatabase.Contains(m))
                    {string path=AssetDatabase.GenerateUniqueAssetPath("Assets/Echo/Materials/Robot-"+(n++)+".mat");AssetDatabase.CreateAsset(m,path);}
        }
        [MenuItem("ECHO/Crear estación isométrica")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");Directory.CreateDirectory("Assets/Echo/Materials");Directory.CreateDirectory("Assets/Echo/Prefabs");
            AssetDatabase.Refresh();materials.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var station=new GameObject("ECHO Station / entorno");root=station.transform;world=station.AddComponent<StationWorld>();
            var floor=Mat("Floor",new Color(.19f,.28f,.33f));
            var wall=Mat("Wall",new Color(.24f,.34f,.4f));
            var dark=Mat("Hull",new Color(.055f,.09f,.125f));
            var trim=Mat("Teal",new Color(.2f,.9f,.8f),true);
            var amber=Mat("Amber",new Color(1,.59f,.18f),true);
            Cube("Casco de la estación",new Vector3(14,-.58f,12),new Vector3(30.4f,.8f,26.4f),dark,0,false);
            world.Tiles=new Renderer[StationWorld.Width*StationWorld.Height];
            for(int z=0;z<StationWorld.Height;z++)for(int x=0;x<StationWorld.Width;x++)
            {
                var tile=Cube("Suelo "+x+","+z,new Vector3(x*2,-.14f,z*2),new Vector3(1.965f,.28f,1.965f),floor,9);
                world.Tiles[z*StationWorld.Width+x]=tile.GetComponent<Renderer>();
                bool boundary=x==0 || z==0 || x==14 || z==12;
                bool dividing=(x==5 && z!=3 && z!=9) || (z==6 && x!=3 && x!=7) || (x==9 && z>6 && z!=9);
                if(boundary || dividing)
                {
                    var p=new Vector2Int(x,z);world.Walls.Add(p);
                    // Cutaway walls stay low on the near sides; collision stays full height.
                    float h=boundary && (z==0 || x==14)? .55f : 1.35f;
                    var w=Cube("Mamparo "+x+","+z,new Vector3(x*2,h*.5f,z*2),new Vector3(1.92f,h,1.92f),wall,8);
                    var col=w.GetComponent<BoxCollider>();col.size=new Vector3(1,1.8f/h,1);col.center=new Vector3(0,(1.8f-h)/(2*h),0);
                    Cube("Luz de mamparo",new Vector3(x*2,h+.025f,z*2),new Vector3(1.8f,.035f,.06f),trim,0,false);
                }
            }
            // Five functional zones; the workshop is the largest lower room.
            Label("01  CARGA",new Vector3(5,.04f,2.4f),new Color(.4f,.9f,.82f));
            Label("02  ALMACÉN",new Vector3(5,.04f,14.4f),new Color(.91f,.74f,.4f));
            Label("03  TALLER",new Vector3(19,.04f,2.4f),new Color(.5f,.76f,.92f));
            Label("04  ENERGÍA",new Vector3(14,.04f,14.4f),new Color(.95f,.6f,.3f));
            Label("05  INVERNADERO",new Vector3(23,.04f,14.4f),new Color(.48f,.91f,.65f),.07f);
            var charge=Prop("charging-dock","charger","Estación de carga",2,2,"light_octogon_off",1.8f);
            Cube("Plataforma de carga",StationWorld.ToWorld(2,2)+Vector3.up*.02f,new Vector3(1.5f,.04f,1.5f),trim,0,false);
            Prop("service-key","key","Llave de servicio",2,10,"box_wires",1.6f);
            var good=Prop("battery-a","battery","Batería A",3,8,"battery_01",2.5f);good.Compatible=true;
            Prop("battery-b","battery","Batería B",2,8,"battery_02",2.5f);
            var door=Prop("greenhouse-door","door","Compuerta del invernadero",9,9,"door_blast-door_closed",3.4f);door.Locked=true;
            Prop("water-pump","pump","Bomba de agua",12,9,"cable_machinery",3.2f);
            Prop("water-valve","valve","Control de riego",11,10,"control_panel",2.4f);
            Prop("cargo-parcel","parcel","Paquete de repuestos",3,10,"crate_companion",1.7f);
            Prop("delivery-terminal","delivery","Terminal de entrega",12,3,"terminal_02",2.7f);
            Decorative("terrarium_01",11,8,2.4f);Decorative("terrarium_01",13,10,2.7f);
            Decorative("bridge_terminal_01",7,10,3.1f);Decorative("box_02",1,8,1.7f);
            Decorative("cart_full_01",10,4,2.3f);Decorative("navigation_table",8,2,2.5f);
            Decorative("ac_unit",1,4,2.4f);Decorative("metal_pipe",13,4,2.2f);
            world.DetourObstacle=Cube("Obstáculo dinámico",StationWorld.ToWorld(StationWorld.DetourCell.x,StationWorld.DetourCell.y)+Vector3.up*.5f,new Vector3(1.5f,1,1.5f),amber,8);
            world.DetourObstacle.SetActive(false);
            var robot=new GameObject("ECHO / robot");robot.transform.position=StationWorld.ToWorld(2,2);world.Robot=robot.transform;
            var motor=robot.AddComponent<PredictiveMotor>();RobotVisual.Build(robot.transform);
            var camObject=new GameObject("Isometric camera");var camera=camObject.AddComponent<Camera>();camObject.tag="MainCamera";
            camera.orthographic=true;camera.orthographicSize=17.8f;camera.nearClipPlane=.1f;camera.farClipPlane=180;
            camera.rect=new Rect(0,.063f,.736f,.861f);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.022f,.037f,.057f);
            camera.allowHDR=false;camera.allowMSAA=true;
            camObject.transform.rotation=Quaternion.Euler(35.26439f,-45,0);camObject.transform.position=new Vector3(14,0,12)-camObject.transform.forward*45;
            var iso=camObject.AddComponent<IsoCamera>();iso.Follow=robot.transform;
            var sun=new GameObject("Key light");var light=sun.AddComponent<Light>();light.type=LightType.Directional;
            light.color=new Color(.77f,.91f,1);light.intensity=1.25f;light.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(55,-25,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.42f,.51f,.62f);
            RenderSettings.fog=false;QualitySettings.antiAliasing=4;QualitySettings.shadowDistance=80;
            var client=station.AddComponent<StationClient>();client.World=world;client.Motor=motor;
            station.AddComponent<StationHUD>().Client=client;
            PersistGeneratedAssets();
            PrefabUtility.SaveAsPrefabAssetAndConnect(robot,"Assets/Echo/Prefabs/EchoRobot.prefab",InteractionMode.AutomatedAction);
            PlayerSettings.companyName="ECHO";PlayerSettings.productName="ECHO Station";PlayerSettings.runInBackground=true;
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=1000;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            Physics.SyncTransforms();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject=robot;
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(new Vector3(14,0,12),Quaternion.Euler(35.26439f,-45,0),24,true);
            Debug.Log("ECHO_STATION_SCENE_READY "+ScenePath);
        }
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);var c=Camera.main;var rect=c.rect;c.rect=new Rect(0,0,1,1);
            var rt=new RenderTexture(1600,1200,24);c.targetTexture=rt;c.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,1200),0,0);texture.Apply();
            Directory.CreateDirectory("Logs");File.WriteAllBytes("Logs/station-preview.png",texture.EncodeToPNG());
            c.targetTexture=null;c.rect=rect;RenderTexture.active=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log("ECHO_STATION_CAPTURE_READY");
        }
        public static void RunIntegration()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying=true;
        }
        [MenuItem("ECHO/Abrir estación")]
        public static void Open()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();game.maximized=true;
            EditorApplication.isPlaying=true;
        }
    }
}
