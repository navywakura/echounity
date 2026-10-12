using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace EchoWorld
{
    // Enabled only in an isolated save directory through Tools/world.py smoke.
    public sealed class PrototypeSmoke : MonoBehaviour
    {
        [Serializable] public class Receipt
        {
            public string scope="BETA 0.0.2 local Play checks. No networking or ECHO certification.";
            public string utc;public bool valid;
            public List<string> passed=new List<string>(),failures=new List<string>();
            public int loadedChunks,loadedColumns,vertices;public long unityAllocatedBytes;public float pathSeconds;
        }
        readonly Receipt report=new Receipt();string output;
        void Check(string name,bool good){if(good)report.passed.Add(name);else report.failures.Add(name);Debug.Log("BETA_CHECK "+name+" "+good);}
        void OnLog(string condition,string stack,LogType type)
        {if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)report.failures.Add(condition);}
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            yield return new WaitForSecondsRealtime(.5f);
        }
        IEnumerator Start()
        {
            Application.logMessageReceived+=OnLog;
            output=PrototypeGame.Argument("--world-check-output",Path.Combine(Application.dataPath,"../Logs"));Directory.CreateDirectory(output);
            PrototypeGame game=GetComponent<PrototypeGame>();
            yield return new WaitForSecondsRealtime(2);
            var p=game.Player;var w=game.World;var session=game.Session;
            Check("initial-stream-bounded-collider-ready",w.LoadedCount>0&&w.LoadedCount<=25&&w.IsColumnReady(0,0));
            Check("isometric-camera-and-no-auto-step",game.MainCamera.orthographic&&p.GetComponent<CharacterController>().stepOffset==0);
            int groundY=w.Data.StandHeight(0,0);
            Vector3Int goal=new Vector3Int(7,w.Data.StandHeight(7,0),0);
            Check("click-route-planned",p.RequestMove(goal));
            float began=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-began<12&&(Mathf.Abs(p.GlobalCell.x-7)>0||Mathf.Abs(p.GlobalCell.z)>0))yield return null;
            report.pathSeconds=Time.realtimeSinceStartup-began;
            Check("click-route-executed-with-collisions",p.GlobalCell.x==7&&p.GlobalCell.z==0);
            p.Teleport(new Vector3Int(0,groundY,0));yield return new WaitForSecondsRealtime(.25f);
            var edit=new Vector3Int(2,groundY,1);p.SelectedBlock=3;int before=session.Count(3);
            Check("place-block-spends-one",p.Edit(edit,true)&&session.Count(3)==before-1);w.BuildImmediate();Physics.SyncTransforms();
            Check("solid-block-is-collidable",Physics.Raycast(w.ToLocal(edit)+new Vector3(.5f,3,.5f),Vector3.down,4,1));
            Check("mine-spawns-drop-before-inventory",p.Edit(edit,false)&&game.Drops.ActiveCount==1&&session.Count(3)==before-1);
            Check("cannot-enclose-uncollected-drop",!p.Edit(edit,true));
            w.BuildImmediate();Physics.SyncTransforms();yield return new WaitForSecondsRealtime(2);
            Check("magnetic-pickup-transfers-exactly-once",game.Drops.ActiveCount==0&&session.Count(3)==before&&game.Drops.Collected==1);
            // An opaque wall must interrupt magnetic attraction; inventory stays unchanged.
            var wall=new Vector3Int(1,groundY,0);w.Data.SetBlock(wall.x,wall.y,wall.z,2);w.NotifyEdit(wall);
            w.Data.SetBlock(wall.x,wall.y+1,wall.z,2);w.NotifyEdit(wall+Vector3Int.up);w.BuildImmediate();Physics.SyncTransforms();
            int wood=session.Count(6);game.Drops.Spawn(new Vector3Int(2,groundY,0),6);
            yield return new WaitForSecondsRealtime(1);
            Check("pickup-does-not-pass-through-wall",session.Count(6)==wood&&game.Drops.ActiveCount==1);
            // Exercise CharacterController against one-block step with no jump input.
            w.Data.SetBlock(wall.x,wall.y+1,wall.z,0);w.NotifyEdit(wall+Vector3Int.up);w.BuildImmediate();Physics.SyncTransforms();
            var motor=p.GetComponent<CharacterController>();float height=p.transform.position.y;
            for(int i=0;i<35;i++){motor.Move(Vector3.right*.06f);yield return null;}
            Check("walking-cannot-climb-one-block",p.transform.position.y<height+.2f&&p.GlobalCell.x==0);
            Check("manual-jump-started",p.TryJump());
            for(int i=0;i<50;i++){motor.Move(Vector3.right*.05f);yield return null;}
            Check("manual-jump-clears-one-block",p.GlobalCell.x>=1);
            w.Data.SetBlock(wall.x,wall.y,wall.z,0);w.NotifyEdit(wall);w.BuildImmediate();Physics.SyncTransforms();
            p.Teleport(new Vector3Int(0,groundY+5,0));session.RestoreVitals();
            began=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-began<8&&(!p.IsGrounded||Time.realtimeSinceStartup-began<.2f))yield return null;
            Check("five-block-physical-fall-damages-two-hp",session.Health==18);
            // Walk from a five-block ledge, save mid-fall and restore that exact motion.
            var ledge=new Vector3Int(0,groundY+4,0);w.Data.SetBlock(ledge.x,ledge.y,ledge.z,2);w.NotifyEdit(ledge);w.BuildImmediate();
            p.Teleport(ledge+Vector3Int.up);session.RestoreVitals();yield return new WaitForSecondsRealtime(.3f);
            for(int i=0;i<30;i++){motor.Move(Vector3.right*.055f);yield return null;}
            if(p.Airborne)
            {
                game.Save();var falling=SessionPersistence.Read(game.SaveRoot,game.Seed);
                p.RestorePosition(falling.cell,falling.offset);p.RestoreFall(falling.airborne,falling.fallApex,falling.vertical);
                Check("fall-state-restored-midair",p.Airborne&&p.FallApex==falling.fallApex&&p.VerticalSpeed==falling.vertical);
            }
            else Check("fall-state-restored-midair",false);
            began=Time.realtimeSinceStartup;while(Time.realtimeSinceStartup-began<6&&!p.IsGrounded)yield return null;
            Debug.Log("LEDGE_FALL distance="+p.LastFallDistance+" damage="+p.LastFallDamage+" health="+session.Health);
            Check("ledge-fall-keeps-five-block-damage-after-resume",session.Health==18);
            w.Data.SetBlock(ledge.x,ledge.y,ledge.z,0);w.NotifyEdit(ledge);w.BuildImmediate();p.Teleport(new Vector3Int(0,groundY,0));
            var rig=game.MainCamera.GetComponent<IsometricCamera>();rig.Rotate(1);
            Vector3 projected=game.MainCamera.WorldToViewportPoint(p.transform.position+Vector3.up);
            Check("camera-quarter-turn-keeps-player-centered",Mathf.Abs(projected.x-.5f)<.01f&&Mathf.Abs(projected.y-.5f)<.01f);rig.Rotate(-1);
            // Player torch has a point light, can be mined, and remains non-solid.
            var torch=new Vector3Int(-2,groundY,1);p.SelectedBlock=5;
            Check("place-torch-item",p.Edit(torch,true));w.BuildImmediate();game.Torches.Invalidate();
            yield return new WaitForSecondsRealtime(1.2f);
            Check("torch-real-point-light-nonsolid",game.Torches.Count>0&&!WorldData.IsSolid(5));
            game.DayNight.SetTime(899.5);Check("day-boundary",game.DayNight.IsDay);
            game.DayNight.SetTime(900);Check("night-boundary",!game.DayNight.IsDay);
            game.DayNight.SetTime(1800);Check("next-day-boundary",game.DayNight.IsDay);
            game.DayNight.SetTime(1350);yield return new WaitForSecondsRealtime(.3f);
            yield return Capture("world-night");
            game.DayNight.SetTime(350);
            // More than 24 torches keep their geometry and picking colliders.
            var extras=new List<Vector3Int>();
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                var c=new Vector3Int(x,groundY+3,z);
                if(extras.Count<28&&w.Data.GetBlock(c.x,c.y,c.z)==0){w.Data.SetBlock(c.x,c.y,c.z,5);w.NotifyEdit(c);extras.Add(c);}
            }
            w.BuildImmediate();game.Torches.Invalidate();yield return new WaitForSecondsRealtime(1.1f);
            Check("torch-cap-limits-lights-not-picking",game.Torches.Count>=28&&game.Torches.ActiveLights==24&&UnityEngine.Object.FindObjectsByType<TorchMarker>(FindObjectsSortMode.None).Length>=28);
            foreach(var c in extras){w.Data.SetBlock(c.x,c.y,c.z,0);w.NotifyEdit(c);}w.BuildImmediate();game.Torches.Invalidate();
            // A placed empty chest stores resources and refuses mining while occupied.
            var chest=new Vector3Int(2,groundY,2);p.SelectedBlock=10;
            Check("place-empty-chest",p.Edit(chest,true)&&game.Chests.Empty(chest));w.BuildImmediate();
            int stones=session.Count(2);
            Check("chest-deposit-conserves-items",game.Chests.DepositOne(chest,2)&&session.Count(2)==stones-1&&game.Chests.Count(chest,2)==1);
            Check("cannot-mine-nonempty-chest",!p.Edit(chest,false));
            Check("chest-withdraw-conserves-items",game.Chests.TakeOne(chest,2)&&session.Count(2)==stones&&game.Chests.Empty(chest));
            game.Hud.OpenChest(chest);Check("chest-opens-inventory",game.Hud.IsInventoryOpen);yield return Capture("world-chest");
            game.Hud.HandleEscape();game.Hud.ToggleInventory();yield return Capture("world-inventory");
            game.Hud.BeginChat();game.Hud.HandleEscape();
            Check("escape-focused-chat-preserves-inventory",!game.Hud.IsTyping&&game.Hud.IsInventoryOpen&&!game.Hud.IsPaused);
            game.Hud.HandleEscape();game.Hud.BeginChat();game.Hud.HandleEscape();
            Check("escape-chat-does-not-pause",!game.Hud.IsTyping&&!game.Hud.IsPaused);
            game.Hud.HandleEscape();double pausedAt=game.DayNight.Elapsed;
            yield return new WaitForSecondsRealtime(.4f);
            Check("pause-freezes-clock",game.Hud.IsPaused&&Time.timeScale==0&&game.DayNight.Elapsed==pausedAt);
            yield return Capture("world-pause");game.Hud.HandleEscape();
            Check("resume-restores-time",!game.Hud.IsPaused&&Time.timeScale==1);
            game.Hud.SubmitChat("La Tierra. Campamento listo.");
            Check("literal-chat-bubble",session.LastMessage=="La Tierra. Campamento listo.");
            Check("xp-locked-level-one",session.Level==1&&session.Xp==0);
            game.Save();var saved=SessionPersistence.Read(game.SaveRoot,game.Seed);
            Check("session-saves-health-items-chest-clock",saved.health==session.Health&&saved.inventory[2]==session.Count(2)&&saved.chests.Length>0&&saved.clock==game.DayNight.Elapsed);
            var reopened=new WorldSaveStore(Path.GetDirectoryName(game.SaveRoot));var reopenedData=new WorldData(game.Seed,reopened.Working);
            Check("checkpoint-reopens-real-terrain-and-inventory",reopenedData.GetBlock(chest.x,chest.y,chest.z)==10&&reopenedData.GetBlock(torch.x,torch.y,torch.z)==5&&SessionPersistence.Read(reopened.Working,game.Seed).inventory[10]==session.Count(10));
            yield return Capture("world-prototype");
            p.Teleport(new Vector3Int(-7,1,-6));yield return new WaitForSecondsRealtime(.3f);
            Check("occluding-house-keeps-physical-roof",Physics.Linecast(game.MainCamera.transform.position,p.transform.position+Vector3.up,1,QueryTriggerInteraction.Ignore));
            yield return Capture("world-cutaway");
            Vector3Int remote=new Vector3Int(1000000,w.Data.StandHeight(1000000,-1000000),-1000000);p.Teleport(remote);
            Check("teleport-collider-synchronized",Physics.Raycast(p.transform.position+Vector3.up*3,Vector3.down,out RaycastHit hit,6,1)&&Mathf.Abs(hit.point.y-remote.y)<.1f);
            yield return new WaitForSecondsRealtime(.3f);
            Check("large-coordinate-origin-rebase",p.GlobalCell.x==remote.x&&p.GlobalCell.z==remote.z&&Mathf.Abs(p.transform.position.x)<32);
            Check("stream-evicts-on-travel",w.LoadedCount<=25&&w.PendingCount<=25);
            if(game.Seed==72831)
            {
                p.Teleport(new Vector3Int(0,w.Data.StandHeight(0,-16),-16));game.DayNight.SetTime(350);
                yield return new WaitForSecondsRealtime(.5f);yield return Capture("world-forest");
            }
            if(game.Seed==733173)
            {
                p.Teleport(new Vector3Int(-13,-3,2));yield return new WaitForSecondsRealtime(.4f);
                yield return Capture("world-cave");
            }
            p.Teleport(new Vector3Int(0,groundY,0));yield return new WaitForSecondsRealtime(.2f);
            // Use the pause button's actual callback with a route and damaged vitals.
            p.RequestMove(goal);session.Damage(3);session.StepStamina(1,true);
            int[] inventoryBeforeRestart=session.SnapshotInventory();
            p.RestoreFall(true,p.transform.position.y+30,-15);
            game.Hud.HandleEscape();double clockBeforeRestart=game.DayNight.Elapsed;
            game.Hud.RestartGame();
            Check("restart-respawns-unpauses-and-cancels-motion",!game.Hud.IsPaused&&Time.timeScale==1
                &&p.GlobalCell==new Vector3Int(0,groundY,0)&&!p.WalkingPath&&!p.Airborne&&p.VerticalSpeed==0
                &&session.Health==LocalSession.MaxHealth&&session.Stamina==LocalSession.MaxStamina);
            bool inventoryKept=true;
            int[] inventoryAfterRestart=session.SnapshotInventory();
            for(int i=0;i<inventoryBeforeRestart.Length;i++)inventoryKept&=inventoryBeforeRestart[i]==inventoryAfterRestart[i];
            Check("restart-keeps-inventory-terrain-and-clock",inventoryKept&&game.DayNight.Elapsed==clockBeforeRestart
                &&w.Data.GetBlock(chest.x,chest.y,chest.z)==10&&w.Data.GetBlock(torch.x,torch.y,torch.z)==5);
            var restartSaved=SessionPersistence.Read(game.SaveRoot,game.Seed);
            Check("restart-saves-camp-and-restored-vitals",restartSaved.cell==new Vector3Int(0,groundY,0)
                &&restartSaved.health==LocalSession.MaxHealth&&restartSaved.stamina==LocalSession.MaxStamina&&!restartSaved.airborne);
            session.Damage(LocalSession.MaxHealth);yield return new WaitForSecondsRealtime(.2f);
            Check("ordinary-death-still-respawns",session.Health==LocalSession.MaxHealth&&p.GlobalCell==new Vector3Int(0,groundY,0));
            report.loadedChunks=w.LoadedCount;report.loadedColumns=w.Data.LoadedColumns;report.vertices=w.VertexCount;
            report.unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong();report.utc=DateTime.UtcNow.ToString("o");report.valid=report.failures.Count==0;
            File.WriteAllText(Path.Combine(output,"play-smoke.json"),JsonUtility.ToJson(report,true));
            Application.logMessageReceived-=OnLog;Debug.Log("WORLD_SMOKE "+(report.valid?"PASS":"FAIL"));
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(report.valid?0:1);
            #endif
        }
    }
}
