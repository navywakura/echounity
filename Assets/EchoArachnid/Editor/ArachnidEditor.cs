using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

namespace EchoStation.Arachnid.Editor
{
    public static class ArachnidEditor
    {
        public const string Model="Assets/EchoArachnid/Models/ECHO5_Arachnid.fbx";
        public const string Prefab="Assets/EchoArachnid/Prefabs/ECHO5_Arachnid.prefab";
        public const string Scene="Assets/Scenes/ECHO5_ArachnidLab.unity";
        static Color Hex(string hex){ColorUtility.TryParseHtmlString("#"+hex,out var c);return c;}
        static Material Make(string name,string hex,float metal,float rough,float emission=0)
        {
            string path="Assets/EchoArachnid/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
            material.color=Hex(hex);material.SetFloat("_Metallic",metal);material.SetFloat("_Glossiness",1-rough);
            if(emission>0){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",Hex(hex)*emission);}
            EditorUtility.SetDirty(material);return material;
        }
        [MenuItem("ECHO/Arácnido/Construir prefab y laboratorio")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/EchoArachnid/Materials");Directory.CreateDirectory("Assets/EchoArachnid/Prefabs");
            AssetDatabase.Refresh();
            var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
            if(!importer)throw new InvalidOperationException("Run Tools/build_arachnid.py in Blender first.");
            importer.globalScale=1;importer.useFileScale=true;importer.importAnimation=false;
            importer.bakeAxisConversion=true;importer.preserveHierarchy=true;importer.isReadable=true;
            importer.importNormals=ModelImporterNormals.Import;importer.SaveAndReimport();
            var materials=new[]{Make("Mat_Chassis_Dark","1E2024",.8f,.35f),Make("Mat_Mechanical_Joints","56616C",.85f,.26f),
                Make("Mat_ECHO_Eye_Emission","00E5FF",.25f,.16f,5),Make("Mat_Copper_Trim","C38152",.78f,.30f),
                Make("Mat_Grip_Rubber","12181C",.05f,.6f),Make("Mat_Armor_Panels","34414A",.8f,.3f),Make("Mat_Optical_Glass","061B22",.55f,.14f)};
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var item=(GameObject)PrefabUtility.InstantiatePrefab(source);item.name="ECHO5_Arachnid";
            // Blender's -Y front is not necessarily Unity's +Z after FBX conversion.
            var lenses=item.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Eye_") && t.name.EndsWith("_Emission")).ToArray();
            if(lenses.Length!=4)throw new InvalidOperationException("Four optical lenses required");
            if(lenses.Average(t=>t.position.z)<0)item.transform.rotation=Quaternion.Euler(0,180,0);
            if(lenses.Average(t=>t.position.z)<.2f)throw new InvalidOperationException("Optical front must face Unity +Z");
            foreach(var renderer in item.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>materials.FirstOrDefault(x=>m && m.name.StartsWith(x.name))??materials[0]).ToArray();
            item.AddComponent<ArachnidGait>();
            var renderers=item.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
            foreach(var rr in renderers)bounds.Encapsulate(rr.bounds);
            if(bounds.size.x<.95f || bounds.size.x>1.15f || Mathf.Abs(bounds.min.y)>.002f)
                throw new InvalidOperationException("FBX unit/ground mismatch: "+bounds);
            var names=item.GetComponentsInChildren<Transform>();
            foreach(string side in new[]{"L","R"})for(int i=0;i<4;i++)foreach(string part in new[]{"Coxa","Femur","Tibia","Foot"})
                if(!names.Any(t=>t.name=="Leg_"+side+i+"_"+part))throw new InvalidOperationException("Missing rig pivot");
            if(names.Count(t=>t.name.StartsWith("Eye_") && t.name.EndsWith("_Emission"))!=4)throw new InvalidOperationException("Four lenses required");
            var prefab=PrefabUtility.SaveAsPrefabAsset(item,Prefab);UnityEngine.Object.DestroyImmediate(item);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var environment=new GameObject("ECHO arachnid laboratory");
            var world=environment.AddComponent<NavigationWorld>();world.VisualPrefab=prefab;world.SensorMountHeight=.30f;
            var client=environment.AddComponent<NavigationClient>();client.World=world;client.Endpoint="http://127.0.0.1:8771";
            var cameraObject=new GameObject("Isometric evaluator camera");cameraObject.tag="MainCamera";
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=15;
            camera.nearClipPlane=.05f;camera.farClipPlane=180;camera.clearFlags=CameraClearFlags.SolidColor;
            cameraObject.transform.rotation=Quaternion.Euler(35.26439f,-45,0);
            var iso=cameraObject.AddComponent<IsoCamera>();iso.Focus=new Vector3(12,0,12);
            cameraObject.transform.position=iso.Focus-cameraObject.transform.forward*45;
            var sun=new GameObject("Soft studio daylight");var key=sun.AddComponent<Light>();key.type=LightType.Directional;
            key.color=new Color(.81f,.91f,1);key.intensity=1.4f;key.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(55,-25,0);
            PlayerSettings.runInBackground=true;
            world.gameObject.AddComponent<ArachnidHUD>().Client=client;
            world.gameObject.AddComponent<ArachnidDemo>().Client=client;
            Camera.main.backgroundColor=Hex("101B24");Camera.main.allowHDR=true;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.62f,.7f);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);
            var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(s=>s.path==Scene))scenes.Add(new EditorBuildSettingsScene(Scene,true));
            EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/Arachnid");
            File.WriteAllText("Logs/Arachnid/unity-import.json",JsonUtility.ToJson(new ImportReport{valid=true,width=bounds.size.x,height=bounds.size.y,depth=bounds.size.z,ground=bounds.min.y,renderers=renderers.Length,pivots=32,eyes=4},true));
            Debug.Log("ECHO_ARACHNID_IMPORT_VALID "+bounds);
        }
        [Serializable] class ImportReport {public bool valid;public float width,height,depth,ground;public int renderers,pivots,eyes;}
        [MenuItem("ECHO/Arácnido/Abrir laboratorio")]
        public static void Open()
        {
            if(!File.Exists(Scene))Build();
            NavigationWorld.NextProfile="cold";
            NavigationWorld.NextCase=NavigationWorld.Cases().First(c=>c.split=="development").id;
            EditorSceneManager.OpenScene(Scene);
            var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();game.maximized=true;
            EditorApplication.isPlaying=true;
        }
        public static void BuildAndOpen(){Build();Open();}
    }
}
