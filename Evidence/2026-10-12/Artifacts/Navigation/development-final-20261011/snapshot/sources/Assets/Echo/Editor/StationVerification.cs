using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EchoStation.Editor
{
    public static class StationVerification
    {
        public static void RunAll()
        {
            EchoStation.MotionVerification.Run();
            Run();
        }
        public static void Run()
        {
            StationEditor.CreateScene();
            var world=UnityEngine.Object.FindFirstObjectByType<StationWorld>();
            var checks=new List<string>();
            void Check(bool value,string name) {if(!value) throw new Exception(name); checks.Add(name);}
            Physics.SyncTransforms();
            var obs=world.Observe("test",0);
            Check(obs.cells.Length<15*13,"partial observation excludes hidden cells");
            Check(Array.TrueForAll(obs.objects,o=>o.kind!="battery"),"distant batteries are not leaked");
            Check(!world.TryInteract("battery-a",false),"cannot pick up distant object");
            var key=world.FindObject("service-key");
            world.PlaceRobot(new Vector2Int(key.X+1,key.Z));
            Check(world.TryInteract(key.Id,false),"key pickup in range");
            var battery=world.FindObject("battery-a");
            world.PlaceRobot(new Vector2Int(battery.X+1,battery.Z));
            Check(!battery.PublicData().compatible,"hidden compatibility stays private");
            Check(world.TryInteract(battery.Id,true),"inspection produces actual compatibility");
            Check(battery.PublicData().compatible,"compatible battery identified by inspection");
            Check(world.TryInteract(battery.Id,false),"battery pickup in range");
            var door=world.FindObject("greenhouse-door");
            world.PlaceRobot(new Vector2Int(door.X-1,door.Z));
            Check(world.TryInteract(door.Id,false) && !door.Locked,"key opens door");
            Check(!world.Blocked(new Vector2Int(door.X,door.Z)),"opening door changes physical navigation");
            var pump=world.FindObject("water-pump");
            world.PlaceRobot(new Vector2Int(pump.X-1,pump.Z));
            Check(world.TryInteract(pump.Id,false) && pump.Active,"battery powers pump");
            var valve=world.FindObject("water-valve");
            world.PlaceRobot(new Vector2Int(valve.X-1,valve.Z));
            Check(world.TryInteract(valve.Id,false) && valve.Active,"valve opens after powered pump");
            Check(!world.Completed,"must return to charger before completion");
            world.PlaceRobot(new Vector2Int(2,2));
            Check(world.TryInteract("charging-dock",false) && world.Completed,"mission judged by consequences and return");
            Check(!world.TryInteract("missing-id",false),"unknown object rejected");
            world.SetDetour(true);
            Check(world.Blocked(StationWorld.DetourCell),"detour blocks the actual storage-energy passage");
            Check(!world.Blocked(new Vector2Int(5,3)) && !world.Blocked(new Vector2Int(7,6)),"alternative workshop route remains physically available");
            world.PlaceRobot(new Vector2Int(2,3));
            world.Robot.position+=Vector3.right*.6f;
            world.RecordOutcome("test",0,"move","",new Vector2Int(2,3),"arrived");
            Check(world.LastOutcome.status=="blocked" && world.LastOutcome.distanceMm>200,"rounded cell cannot falsely confirm physical arrival");
            world.PlaceRobot(new Vector2Int(2,2));
            var client=UnityEngine.Object.FindFirstObjectByType<StationClient>();
            int beforeActions=world.Actions;float beforeEnergy=world.Energy;
            client.StartManual(new Vector2Int(2,3));
            for(int i=0;i<240;i++){client.Motor.Tick(.02f);Physics.SyncTransforms();}
            client.Pause();
            Check(world.Actions==beforeActions+1 && world.Energy<beforeEnergy,"manual movement charges work and energy exactly once");
            Check(world.LastOutcome.status=="arrived" && world.LastOutcome.session=="manual","manual arrival has a physical receipt");
            beforeActions=world.Actions;
            client.StartManual(new Vector2Int(2,4));client.Motor.Tick(.02f);client.Pause();
            Check(world.Actions==beforeActions+1 && world.LastOutcome.status=="cancelled","pause finalizes an interrupted movement");
            client.StartManual(new Vector2Int(2,4));
            for(int i=0;i<240;i++){client.Motor.Tick(.02f);Physics.SyncTransforms();}
            client.Pause();
            Check(world.LastOutcome.status=="arrived","new user command can retry a terminal target");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/world-tests.json","{\"valid\":true,\"checks\":"+checks.Count+",\"names\":[\""+string.Join("\",\"",checks)+"\"]}");
            Debug.Log("WORLD CHECKS PASSED "+checks.Count);
        }
    }
}
