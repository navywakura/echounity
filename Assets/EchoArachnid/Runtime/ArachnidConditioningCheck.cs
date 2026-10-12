using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace EchoStation.Arachnid
{
    public sealed class ArachnidConditioningCheck : MonoBehaviour
    {
        [Serializable] public class Trial {public string stage,session,journal,action;public int collisions,moves,cam,destroyed;public bool passed,acknowledged;}
        [Serializable] public class Report {public bool valid;public string scope="PhysX controlled proposal, not autonomous route selection";public Trial[] trials;}
        [Serializable] class Finish {public string journal;public bool pending;public NavBasalState basal;}
        NavigationWorld world;string endpoint,folder;NavDecision answer;Finish final;string fault;
        IEnumerator Post(string route,NavObservation obs)
        {
            using(var q=new UnityWebRequest(endpoint+route,"POST"))
            {
                q.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(obs)));
                q.downloadHandler=new DownloadHandlerBuffer();q.SetRequestHeader("Content-Type","application/json");q.timeout=15;
                yield return q.SendWebRequest();
                if(q.result!=UnityWebRequest.Result.Success){fault=q.responseCode+" "+q.downloadHandler.text;yield break;}
                if(route.EndsWith("finish"))final=JsonUtility.FromJson<Finish>(q.downloadHandler.text);
                else answer=JsonUtility.FromJson<NavDecision>(q.downloadHandler.text);
            }
        }
        IEnumerator Move(NavDecision d)
        {
            var before=world.Position;var target=world.Execute(d.command);float until=Time.realtimeSinceStartup+15;
            while(world.Motor.HasTarget && Time.realtimeSinceStartup<until)yield return null;
            world.Record(d.session,d.seq,"move",d.command,before,target,false);
        }
        IEnumerator Restart(string stage)
        {
            File.WriteAllText(Path.Combine(folder,"restart-request.txt"),stage);
            float until=Time.realtimeSinceStartup+45;
            string ready=Path.Combine(folder,"restart-ready.txt");
            while(Time.realtimeSinceStartup<until)
            {
                if(File.Exists(ready) && File.ReadAllText(ready)==stage)yield break;
                yield return new WaitForSecondsRealtime(.1f);
            }
            fault="server restart not acknowledged";
        }
        IEnumerator RunTrial(string stage,Vector2Int origin,bool remove,bool expectVeto)
        {
            world.Place(origin);world.LastOutcome=new NavOutcome();world.Sensor.SetYaw(270);
            if(remove)world.SetObstacle(new Vector2Int(3,4),false);
            int beforeBlocks=world.Blocks,beforeMoves=world.Moves;
            string token=Guid.NewGuid().ToString("N");int seq=0;
            yield return Post("/navigate",world.Observe(token,seq,"cold"));
            if(fault!=null)yield break;
            string action=answer.action;
            if(action=="move"){yield return Move(answer);seq++;}
            yield return Post("/navigate/finish",world.Observe(token,seq,"cold"));
            var trial=new Trial{stage=stage,session=token,journal=final?.journal,action=action,
                collisions=world.Blocks-beforeBlocks,moves=world.Moves-beforeMoves,cam=final?.basal?.camUsed??0,
                destroyed=final?.basal?.camDestroyed??-1,acknowledged=fault==null && final!=null && !final.pending};
            trial.passed=trial.acknowledged && trial.destroyed==0 &&
                (expectVeto? action=="wait" && trial.collisions==0 && trial.moves==0:
                 action=="move" && (stage=="no_cam_control"?trial.collisions==1:trial.collisions==0 && world.Position==origin+Vector2Int.left));
            runs.Add(trial);Save(false);
        }
        readonly List<Trial> runs=new List<Trial>();
        void Save(bool valid){File.WriteAllText(Path.Combine(folder,"conditioning-physical.json"),JsonUtility.ToJson(new Report{valid=valid,trials=runs.ToArray()},true));}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();folder=args[Array.IndexOf(args,"-closureOutput")+1];Directory.CreateDirectory(folder);
            var client=FindFirstObjectByType<NavigationClient>();endpoint=client.Endpoint;world=client.World;
            Time.timeScale=6;Application.targetFrameRate=120;
            var walls=new List<NavPoint>();for(int x=0;x<9;x++)for(int z=0;z<9;z++)if(x==0||z==0||x==8||z==8)walls.Add(new NavPoint(x,z));
            world.Build(new NavWorldCase{id="conditioning-physical-v1",split="development",family="rooms",width=9,height=9,
                start=new NavPoint(6,4),goal=new NavPoint(1,1),walls=walls.ToArray(),
                mapping=new[]{new NavPoint(-1,0),new NavPoint(0,1),new NavPoint(1,0),new NavPoint(0,-1)}});
            // Acquire two independent unit movements, then add a real obstacle AFTER the proposal.
            string token=Guid.NewGuid().ToString("N");
            for(int seq=0;seq<3;seq++)
            {
                yield return Post("/navigate",world.Observe(token,seq,"cold"));
                if(fault!=null || answer.action!="move"){fault=fault??"initial probe not move";break;}
                if(seq==2)world.SetObstacle(new Vector2Int(3,4),true);
                yield return Move(answer);
            }
            if(fault==null)
            {
                var obs=world.Observe(token,3,"cold");yield return Post("/navigate",obs);
                yield return Post("/navigate/finish",obs);
                runs.Add(new Trial{stage="first_encounter",session=token,journal=final?.journal,action=answer?.action,
                    collisions=world.Blocks,moves=world.Moves,cam=final?.basal?.camUsed??0,destroyed=final?.basal?.camDestroyed??-1,
                    acknowledged=fault==null,passed=fault==null && world.Blocks==1 && answer.action=="wait"});
                Save(false);
                yield return RunTrial("second_encounter",new Vector2Int(4,4),false,true);
                yield return Restart("retention");
                if(fault==null)yield return RunTrial("cross_session_restart",new Vector2Int(4,4),false,true);
                if(fault==null)yield return RunTrial("similar_safe_context",new Vector2Int(4,2),false,false);
                if(fault==null)yield return RunTrial("obstacle_removed",new Vector2Int(4,4),true,false);
                world.Place(new Vector2Int(4,4));world.SetObstacle(new Vector2Int(3,4),true);
                yield return Restart("control");
                if(fault==null)yield return RunTrial("no_cam_control",new Vector2Int(4,4),false,false);
            }
            bool valid=fault==null && runs.Count==6 && runs.All(r=>r.passed);
            Save(valid);if(fault!=null)File.WriteAllText(Path.Combine(folder,"conditioning-error.txt"),fault);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(valid?0:1);
#else
            Application.Quit(valid?0:1);
#endif
        }
    }
}
