using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

public static class FlightBuild {
    public static void Build() {
        if(FlightCoordinates.Position(new[]{3f,2f,1f})!=new Vector3(3,1,2)) throw new Exception("ENU/Unity mismatch");
        if(Vector3.Distance(FlightCoordinates.Attitude(new[]{0f,0f,0f})*Vector3.forward,Vector3.right)>.001f) throw new Exception("Attitude mismatch");
        if(FlightCoordinates.Valid(new FlightFrame())) throw new Exception("Missing frame accepted");
        foreach(var terminal in new[]{"MISSION_COMPLETE","RETURNED_PARTIAL","CONTINGENCY_LANDED","LANDING_INCOMPLETE"})
            if(!FlightCoordinates.Terminal(terminal)) throw new Exception("Terminal flight still animated");
        if(FlightCoordinates.Terminal("EXPLORE")) throw new Exception("Exploration marked terminal");
        // Keep dynamically-created geometry shaders in the standalone build.
        Directory.CreateDirectory("Assets/Resources");
        foreach(var pair in new[]{new[]{"FlightSurface","Standard"},new[]{"FlightLine","Sprites/Default"}}) {
            string matPath="Assets/Resources/"+pair[0]+".mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(matPath)==null) {
                var shader=Shader.Find(pair[1]); if(shader==null) throw new Exception("Missing shader "+pair[1]);
                AssetDatabase.CreateAsset(new Material(shader),matPath);
            }
        }
        AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Flight Lab").AddComponent<FlightViewer>();
        Directory.CreateDirectory("Assets/Scenes"); EditorSceneManager.SaveScene(scene,"Assets/Scenes/FlightLab.unity");
        PlayerSettings.companyName="RxLabs"; PlayerSettings.productName="ECHO Flight Lab";
        PlayerSettings.defaultScreenWidth=1280; PlayerSettings.defaultScreenHeight=800;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed; PlayerSettings.runInBackground=true;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        string path=Environment.GetEnvironmentVariable("FLIGHTLAB_BUILD") ?? "Build/EchoFlightLab.x86_64";
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/FlightLab.unity"},locationPathName=path,target=BuildTarget.StandaloneLinux64,options=BuildOptions.None });
        File.WriteAllText("build-result.json",JsonUtility.ToJson(new Receipt { status=report.summary.result.ToString(),errors=report.summary.totalErrors,size=report.summary.totalSize,coordinateTests=3,stateTests=5 },true));
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Build failed");
    }
    [Serializable] class Receipt { public string status; public int errors; public ulong size; public int coordinateTests,stateTests; }
}
