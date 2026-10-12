using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EchoStation.Editor
{
    public static class NavigationVerification
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var world=new GameObject("navigation fixture").AddComponent<NavigationWorld>();
            var c=new NavWorldCase {id="fixture",split="development",family="rooms",width=7,height=7,
                start=new NavPoint(3,3),goal=new NavPoint(5,5),walls=new[]{new NavPoint(2,3)},
                mapping=new[]{new NavPoint(-1,0),new NavPoint(0,1),new NavPoint(1,0),new NavPoint(0,-1)}};
            world.Build(c);Physics.SyncTransforms();var checks=new List<string>();
            void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
            Check(world.Robot && world.Sensor && world.Sensor.View,"robot and physical camera created");
            world.Sensor.SetYaw(270);var o=world.Observe("probe",0,"cold");string json=JsonUtility.ToJson(o);
            Check(o.rays.Length==61,"camera depth measurement count");
            Check(Array.Exists(o.rays,r=>r.hit),"real wall observed in frustum");
            Check(!json.Contains("walls") && !json.Contains("mapping") && !json.Contains("family"),"private map and actuators excluded from observation");
            Check(!world.Completed,"goal is not satisfied by a prediction");
            world.Place(new Vector2Int(5,5));
            Check(world.Completed,"physical arrival satisfies waypoint judge");
            world.Place(new Vector2Int(3,3));var target=world.Execute(0);
            Check(target==new Vector2Int(2,3),"environment applies opaque actuator handle");
            for(int i=0;i<500;i++){world.Motor.Tick(.02f);Physics.SyncTransforms();if(!world.Motor.HasTarget)break;}
            Check(world.Motor.Blocked && world.Position!=target,"actual physics blocks an occupied destination");
            world.Record("probe",0,"move",0,new Vector2Int(3,3),target,false);
            Check(world.LastOutcome.status=="blocked" && world.Blocks==1,"failed movement has an honest receipt");
            Check(world.SetObstacle(new Vector2Int(4,4),true),"new obstacle can change the world");
            Check(!world.SetObstacle(world.Position,true),"obstacle cannot spawn inside robot");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/navigation-world-tests.json","{\"valid\":true,\"checks\":"+checks.Count+",\"names\":[\""+string.Join("\",\"",checks)+"\"]}");
            UnityEngine.Object.DestroyImmediate(world.gameObject);
        }
    }
}
