using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    // Explicit physical integration test, never a source of priors for the actor.
    public sealed class NavigationRunCheck : MonoBehaviour
    {
        [Serializable] class Run
        {
            public string world,profile,session,status,modelHash;
            public bool success,valid,obstacleInjected,terminalAcknowledged,terminalCompleted,terminalPending,modelSaved,pausePreservedSession;
            public int actions,moves,scans,blocks,learnedActions,rays;
            public int[] support;
            public float seconds;
        }
        [Serializable] class Receipt {public bool valid;public string scope="Physical Unity RGB-D navigation: acquisition and development only";public Run[] runs;}
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Time.timeScale=6;Application.targetFrameRate=120;
            var runs=new List<Run>();var cases=NavigationWorld.Cases();
            var selected=cases.Where(c=>c.split=="acquisition").Concat(cases.Where(c=>c.split=="development").Take(3)).ToArray();
            foreach(var c in selected)
            {
                string profile=c.split=="acquisition"?"learn":"trained";
                NavigationWorld.NextCase=c.id;NavigationWorld.NextProfile=profile;
                SceneManager.LoadScene("NavigationLab");yield return null;yield return null;yield return new WaitForSecondsRealtime(.5f);
                var client=FindFirstObjectByType<NavigationClient>();
                bool injected=false;
                if(c==selected.First(item=>item.split=="development"))
                    client.MovementPrepared+=target=>
                    {
                        if(!injected && StillReachable(c,client.World.Position,target))
                            injected=client.World.SetObstacle(target,true);
                    };
                client.Begin();float begin=Time.realtimeSinceStartup;
                bool pauseTested=false,pausePreserved=true;
                while(client.Automatic && Time.realtimeSinceStartup-begin<240)
                {
                    if(runs.Count==0 && !pauseTested && client.Sequence>=2 && !client.Pending)
                    {
                        pauseTested=true;string token=client.Session;int seq=client.Sequence;
                        client.Pause();yield return new WaitForSecondsRealtime(.2f);
                        pausePreserved=!client.Automatic && client.Session==token && client.Sequence==seq;
                        client.Begin();pausePreserved&=client.Session==token && client.Sequence==seq;
                    }
                    yield return null;
                }
                if(client.Automatic)client.StopSession();
                float closingDeadline=Time.realtimeSinceStartup+49;
                while(!client.TerminalAcknowledged && Time.realtimeSinceStartup<closingDeadline)yield return null;
                var d=client.Latest;
                var run=new Run {world=c.id,profile=profile,session=client.Session,status=client.Status,
                    modelHash=d?.modelHash??"",success=client.World.Completed,actions=client.Sequence,
                    moves=client.World.Moves,scans=client.World.Scans,blocks=client.World.Blocks,learnedActions=d?.learnedActions??0,support=d?.support,
                    seconds=Time.realtimeSinceStartup-begin,rays=client.World.Sensor.CaptureDepth().Length,obstacleInjected=injected,terminalAcknowledged=client.TerminalAcknowledged,
                    terminalCompleted=client.TerminalCompleted,terminalPending=client.TerminalPending,modelSaved=client.ModelSaved,
                    pausePreservedSession=runs.Count!=0 || (pauseTested && pausePreserved)};
                bool interventionRequired=c==selected.First(item=>item.split=="development");
                run.valid=run.success && run.rays==61 && run.learnedActions==4 && run.terminalAcknowledged && run.terminalCompleted && !run.terminalPending &&
                    (runs.Count!=0 || run.modelSaved) && run.pausePreservedSession && (!interventionRequired || (injected && run.blocks>0));
                runs.Add(run);client.StopSession();yield return new WaitForSecondsRealtime(.5f);
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/navigation-integration-progress.json",JsonUtility.ToJson(new Receipt{runs=runs.ToArray()},true));
                Debug.Log("NAVIGATION_INTEGRATION "+JsonUtility.ToJson(run));
                if(!run.valid)break;
            }
            bool valid=runs.Count==selected.Length && runs.All(r=>r.valid);
            File.WriteAllText("Logs/navigation-integration-tests.json",JsonUtility.ToJson(new Receipt{valid=valid,runs=runs.ToArray()},true));
            Time.timeScale=1;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(valid?0:1);
#else
            Application.Quit(valid?0:1);
#endif
        }
        // Judge-only reachability prevents an intervention from making the task impossible.
        internal static bool StillReachable(NavWorldCase c,Vector2Int start,Vector2Int changed,IEnumerable<Vector2Int> walls=null)
        {
            if(changed==start || changed==new Vector2Int(c.goal.x,c.goal.z))return false;
            var occupied=new HashSet<Vector2Int>(walls??c.walls.Select(p=>new Vector2Int(p.x,p.z)));
            if(!occupied.Add(changed))return false;
            var queue=new Queue<Vector2Int>();queue.Enqueue(start);var seen=new HashSet<Vector2Int>{start};
            while(queue.Count>0)
            {
                var p=queue.Dequeue();if(p==new Vector2Int(c.goal.x,c.goal.z))return true;
                foreach(var delta in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                {var next=p+delta;if(next.x<0 || next.y<0 || next.x>=c.width || next.y>=c.height || occupied.Contains(next) || !seen.Add(next))continue;queue.Enqueue(next);}
            }
            return false;
        }
    }
}
