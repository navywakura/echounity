using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoStation.Editor
{
    public static class NavigationEditor
    {
        public const string ScenePath="Assets/Scenes/NavigationLab.unity";
        [MenuItem("ECHO/Crear laboratorio de navegación")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var environment=new GameObject("ECHO navigation laboratory");
            var world=environment.AddComponent<NavigationWorld>();
            var client=environment.AddComponent<NavigationClient>();client.World=world;
            environment.AddComponent<NavigationHUD>().Client=client;
            var cameraObject=new GameObject("Isometric evaluator camera");cameraObject.tag="MainCamera";
            var c=cameraObject.AddComponent<Camera>();c.orthographic=true;c.orthographicSize=15;
            c.nearClipPlane=.1f;c.farClipPlane=180;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.022f,.037f,.057f);
            c.allowHDR=false;c.rect=new Rect(0,.27f,.75f,.65f);
            cameraObject.transform.rotation=Quaternion.Euler(35.26439f,-45,0);
            var iso=cameraObject.AddComponent<IsoCamera>();iso.Focus=new Vector3(12,0,12);
            cameraObject.transform.position=iso.Focus-cameraObject.transform.forward*45;
            var sun=new GameObject("Laboratory key light");var light=sun.AddComponent<Light>();light.type=LightType.Directional;
            light.color=new Color(.77f,.91f,1);light.intensity=1.3f;light.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(55,-25,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.45f,.55f,.65f);RenderSettings.fog=false;
            PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1640;PlayerSettings.defaultScreenHeight=980;
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true),new EditorBuildSettingsScene(StationEditor.ScenePath,true)};
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("NAVIGATION_SCENE_READY");
        }
        public static void RunChecks()
        {
            SensorVerification.Run();NavigationVerification.Run();CreateScene();
        }
        public static void RunIntegration(){EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;}
        [MenuItem("ECHO/Abrir navegación aprendida")]
        public static void Open()
        {
            if(!File.Exists(ScenePath))CreateScene();
            EditorSceneManager.OpenScene(ScenePath);
            var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();game.maximized=true;
            EditorApplication.isPlaying=true;
        }
    }
}
