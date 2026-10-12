using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace EchoStation.Arachnid.Editor
{
    public static class ClosureEditor
    {
        // Publication setup retains the redistributable placeholder props.
        public static void PreparePublicProject()
        {
            IntegrateStation();
            Prepare();
        }
        [MenuItem("ECHO/Arácnido/Integrar cuerpo en Station")]
        public static void IntegrateStation()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EchoStation.unity");
            var world=UnityEngine.Object.FindFirstObjectByType<StationWorld>();
            if(!world || !world.Robot)throw new InvalidOperationException("Station body missing");
            if(PrefabUtility.IsPartOfPrefabInstance(world.Robot.gameObject))
                PrefabUtility.UnpackPrefabInstance(world.Robot.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var old in world.Robot.GetComponentsInChildren<RobotVisual>())UnityEngine.Object.DestroyImmediate(old.gameObject);
            if(!world.Robot.GetComponentInChildren<ArachnidGait>())
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ArachnidEditor.Prefab);
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab,world.Robot);
                visual.transform.localPosition=Vector3.zero;
            }
            var client=UnityEngine.Object.FindFirstObjectByType<StationClient>();
            client.Endpoint="http://127.0.0.1:8771";
            if(!client.GetComponent<StationArachnid>())client.gameObject.AddComponent<StationArachnid>();
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
            var scenes=EditorBuildSettings.scenes.ToList();
            foreach(var path in new[]{"Assets/Scenes/EchoStation.unity",ArachnidEditor.Scene,"Assets/Scenes/NavigationLab.unity"})
                if(File.Exists(path) && !scenes.Any(s=>s.path==path))scenes.Add(new EditorBuildSettingsScene(path,true));
            EditorBuildSettings.scenes=scenes.ToArray();
            Debug.Log("ECHO_STATION_ARACHNID_INTEGRATED");
        }
        public static void Prepare()
        {
            EchoStation.MotionVerification.Run();
            EchoStation.Editor.StationVerification.Run(false);
            var world=UnityEngine.Object.FindFirstObjectByType<StationWorld>();
            if(!world.Robot.GetComponentInChildren<ArachnidGait>())
                throw new InvalidOperationException("Install the arachnid with ECHO > Arácnido > Integrar cuerpo en Station before verification.");
            // Dispose scene objects while the scripting/selection managers still exist.
            // Immediate -quit during asset import can tear those managers down first.
            Selection.objects=Array.Empty<UnityEngine.Object>();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            AssetDatabase.SaveAssets();
            if(Application.isBatchMode)EditorApplication.delayCall+=()=>EditorApplication.Exit(0);
        }
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();
            int i=Array.IndexOf(args,"-closureDataset");
            if(i>=0)NavigationWorld.DatasetOverride=Path.GetFullPath(args[i+1]);
            NavigationWorld.NextProfile="cold";
            NavigationWorld.NextCase=NavigationWorld.Cases()[0].id;
            EditorSceneManager.OpenScene(ArachnidEditor.Scene);
            EditorApplication.isPlaying=true;
        }
    }
}
