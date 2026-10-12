using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

namespace EchoWorld.Editor
{
    public static class GameplayTests
    {
        static void Assert(bool good,string name){if(!good)throw new InvalidOperationException(name);}
        public static List<string> RunAll()
        {
            var passed=new List<string>();var session=new LocalSession();
            Assert(!session.Say(" \n\t ",0),"blank message rejected");
            Assert(session.Say("<b>Hola</b>\n",2)&&session.LastMessage=="<b>Hola</b>","chat text retained literally, controls removed");
            Assert(session.MessageExpires==10,"chat expires");
            Assert(LocalSession.CleanChat(new string('x',500)).Length==140,"chat bounded");
            for(int i=0;i<40;i++)session.Say("Mensaje "+i,i);
            Assert(session.ChatHistory.Count==4&&session.ChatHistory.First()=="Mensaje 36","chat history bounded");
            passed.Add("chat/control-characters-length-history-and-expiry");
            int rock=session.Count(2),wood=session.Count(6),torches=session.Count(5);
            Assert(session.CraftTorches(),"craft succeeds with resources");
            Assert(session.Count(2)==rock-1&&session.Count(6)==wood-1&&session.Count(5)==torches+4,"recipe conserves ingredients");
            while(session.Take(6)){}
            rock=session.Count(2);torches=session.Count(5);
            Assert(!session.CraftTorches()&&session.Count(2)==rock&&session.Count(5)==torches,"failed craft atomic");
            Assert(!session.Take(0)&&!session.Take(255)&&session.Count(255)==0,"invalid item IDs cannot spend");
            Assert(!session.Take(2,-1),"negative transfer rejected");
            int accepted=session.Add(2,2000);
            Assert(session.Count(2)==999&&accepted==999-rock&&session.Add(2)==0,"full inventory returns unaccepted items");
            session.Add(6);session.Add(5,2000);wood=session.Count(6);rock=session.Count(2);
            Assert(!session.CraftTorches()&&session.Count(6)==wood&&session.Count(2)==rock,"full output slot preserves ingredients");
            Assert(LocalSession.SlotCount==10&&session.SnapshotInventory().Length==11,"ten distinct item slots");
            passed.Add("inventory/ten-slots-atomic-recipe-full-stack-overflow");
            Assert(LocalSession.FallDamage(4.999f)==0&&LocalSession.FallDamage(5)==2&&LocalSession.FallDamage(8)==8&&LocalSession.FallDamage(14)==20,"fall thresholds 5+ blocks");
            Assert(session.Land(8)==8&&session.Health==12&&session.Land(100)==12&&session.Health==0,"health damage clamps at zero");
            session.RestoreVitals();session.StepStamina(6,true);
            Assert(session.Stamina==0&&session.SprintExhausted&&!session.SpendJump(),"sprint exhaustion prevents jump");
            session.StepStamina(1,false);Assert(session.SprintExhausted,"exhaustion recovery hysteresis");
            session.StepStamina(1,false);Assert(!session.SprintExhausted&&session.SpendJump()&&session.Stamina==18,"rest enables manual jump");
            Assert(session.Level==1&&session.Xp==0,"XP stays locked at level one");
            passed.Add("vitals/fall-damage-stamina-recovery-and-locked-xp");
            Assert(DayNightCycle.Phase(900)==900&&DayNightCycle.Phase(1800)==0&&DayNightCycle.DaySeconds==900&&DayNightCycle.NightSeconds==900,"15 minute day and night");
            passed.Add("clock/900-second-day-900-second-night");
            Persistence();passed.Add("session/atomic-save-roundtrip-corruption-and-seed-rejection");
            CompleteCheckpoint();passed.Add("session/terrain-inventory-checkpoint-isolation-and-recovery");
            CameraOrigin();passed.Add("camera/picking-ray-preserved-across-origin-shift");return passed;
        }
        static void Persistence()
        {
            string root=Path.Combine(Path.GetTempPath(),"echo-beta-session-"+Guid.NewGuid().ToString("N"));
            try
            {
                var session=new LocalSession();session.Land(6);
                var record=new SessionRecord{seed=19,cell=new Vector3Int(-1000000,3,1000000),offset=new Vector3(.45f,.2f,.7f),
                    inventory=session.SnapshotInventory(),health=session.Health,stamina=73,clock=935,chests=new[]{new ChestRecord{cell=new Vector3Int(1,2,3)}},
                    drops=new[]{new DropRecord{cell=new Vector3Int(2,5,3),offset=Vector3.one*.5f,item=6,count=4}}};
                SessionPersistence.Write(root,record);record.clock=1234;SessionPersistence.Write(root,record);
                var restored=SessionPersistence.Read(root,19);
                Assert(restored.health==16&&restored.stamina==73&&restored.clock==1234&&restored.drops[0].count==4&&restored.cell.x==-1000000,"full session roundtrip");
                Assert(File.Exists(Path.Combine(root,"session-v2.json.bak")),"atomic update retains prior record");
                bool rejected=false;try{SessionPersistence.Read(root,20);}catch(InvalidDataException){rejected=true;}
                Assert(rejected,"different seed rejected");
                restored.inventory[2]=-1;rejected=false;
                try{SessionPersistence.Write(root,restored);}catch(ArgumentException){rejected=true;}
                Assert(rejected&&SessionPersistence.Read(root,19).inventory[2]>=0,"invalid snapshot cannot overwrite good save");
                restored.inventory[2]=0;restored.offset.x=float.NaN;rejected=false;
                try{SessionPersistence.Write(root,restored);}catch(InvalidDataException){rejected=true;}
                Assert(rejected,"nonfinite position rejected");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static void CompleteCheckpoint()
        {
            string root=Path.Combine(Path.GetTempPath(),"echo-beta-checkpoint-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new WorldSaveStore(root);var world=new WorldData(4,store.Working);var cell=new Vector3Int(2,1,2);
                var r=new SessionRecord{seed=4,inventory=new LocalSession().SnapshotInventory(),cell=Vector3Int.up,offset=Vector3.one*.5f,
                    chests=new ChestRecord[0],drops=new DropRecord[0]};
                world.SetBlock(cell.x,cell.y,cell.z,3);world.SaveAll();r.inventory[3]--;
                SessionPersistence.Write(store.Working,r);store.Commit();
                // Replace alloy with glass, but do not publish this generation yet.
                world.SetBlock(cell.x,cell.y,cell.z,4);world.SaveAll();r.inventory[3]++;r.inventory[4]--;
                r.airborne=true;r.fallApex=9;r.vertical=-12;SessionPersistence.Write(store.Working,r);
                var recovered=new WorldSaveStore(root);var oldWorld=new WorldData(4,recovered.Working);
                Assert(oldWorld.GetBlock(cell.x,cell.y,cell.z)==3&&SessionPersistence.Read(recovered.Working,4).inventory[3]==23,"uncommitted state cannot split actual terrain and inventory");
                Directory.CreateDirectory(Path.Combine(root,"CURRENT.tmp"));bool failed=false;
                try{store.Commit();}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
                Assert(failed,"injected commit write failure");Directory.Delete(Path.Combine(root,"CURRENT.tmp"));
                var stillOld=new WorldSaveStore(root);
                Assert(SessionPersistence.Read(stillOld.Working,4).inventory[3]==23,"failed commit leaves previous generation visible");
                store.Commit();var final=new WorldSaveStore(root);var state=SessionPersistence.Read(final.Working,4);var finalWorld=new WorldData(4,final.Working);
                Assert(state.inventory[3]==24&&state.inventory[4]==11&&state.airborne&&state.fallApex==9&&state.vertical==-12&&finalWorld.GetBlock(cell.x,cell.y,cell.z)==4,"successful commit publishes actual terrain session and falling state together");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static void CameraOrigin()
        {
            var root=new GameObject("Camera origin check");
            var cameraObject=new GameObject("Picking camera");
            try
            {
                var world=root.AddComponent<ChunkWorld>();
                var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;
                camera.transform.position=new Vector3(12,25,8);
                camera.transform.rotation=Quaternion.Euler(35.264f,45,0);
                var rig=cameraObject.AddComponent<IsometricCamera>();rig.World=world;
                Ray before=camera.ViewportPointToRay(new Vector3(.4f,.6f,0));
                world.Origin=new Vector2Int(160,-144);rig.SynchronizeOrigin();
                Ray after=camera.ViewportPointToRay(new Vector3(.4f,.6f,0));
                Assert(Vector3.Distance(before.origin,after.origin+new Vector3(160,0,-144))<.001f,"picking origin follows rebase before LateUpdate");
                Assert(Vector3.Distance(before.direction,after.direction)<.001f,"picking direction unchanged");
                Vector3 once=camera.transform.position;rig.SynchronizeOrigin();
                Assert(camera.transform.position==once,"repeated synchronization does not shift twice");
            }
            finally{UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
