using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoWorld.Editor
{
    public static class PrototypeEditor
    {
        const string ScenePath="Assets/Scenes/WorldPrototype.unity";
        [Serializable] class Verification
        {
            public string utc;public bool valid;public string scope="Prototype development checks; no ECHO or MMO claim";
            public List<string> passed=new List<string>();public List<string> failures=new List<string>();
        }
        [MenuItem("World Prototype/Create scene")]
        public static void BuildScene()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("World prototype").AddComponent<PrototypeGame>();
            Directory.CreateDirectory("Assets/Scenes");EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="ECHO";PlayerSettings.productName="Echo MMO Prototype";PlayerSettings.bundleVersion="0.0.2";
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Linear;
            AssetDatabase.SaveAssets();
        }
        public static void Verify()
        {
            var receipt=new Verification{utc=DateTime.UtcNow.ToString("o")};
            Application.LogCallback capture=(message,stack,type)=>
            {
                if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)
                    receipt.failures.Add(message+"\n"+stack);
            };
            Application.logMessageReceived+=capture;
            try
            {
                receipt.passed.AddRange(DataTests.RunAll());
                receipt.passed.AddRange(RenderingTests.RunAll());
                receipt.passed.AddRange(GameplayTests.RunAll());
                receipt.passed.AddRange(HudTests.RunAll());
                BuildScene();receipt.passed.Add("project/scene-created");receipt.valid=receipt.failures.Count==0;
            }
            catch(Exception ex){receipt.failures.Add(ex.ToString());Debug.LogException(ex);}
            finally{Application.logMessageReceived-=capture;}
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/verification.json",JsonUtility.ToJson(receipt,true));
            Debug.Log("WORLD_VERIFY "+(receipt.valid?"PASS":"FAIL")+" "+receipt.passed.Count);
            if(Application.isBatchMode)EditorApplication.Exit(receipt.valid?0:1);
        }
        [MenuItem("World Prototype/Open and play")]
        public static void Open()
        {
            if(!File.Exists(ScenePath))BuildScene();else EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.delayCall+=()=>
            {
                Type type=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                if(type!=null){var window=EditorWindow.GetWindow(type);window.Show();window.Focus();window.maximized=true;}
                EditorApplication.isPlaying=true;
            };
        }
    }
}
