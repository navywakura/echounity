using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation.Arachnid
{
    // Opt-in physical development check; never supplies a plan or hidden state to ECHO.
    public sealed class ArachnidRecoveryCheck : MonoBehaviour
    {
        [Serializable] class Run
        {
            public string world,session,journal,reason,state;
            public bool completed,acknowledged,pending,manualPausePreserved,stopIsNotPause;
            public int decisions,moves,blocks,work,cam;
            public float seconds;
        }
        [Serializable] class Report
        {
            public bool valid,isolatingProbeRejected,restartAtSpawn;
            public string scope="Eight development worlds, cold acquisition, physical Unity navigation";
            public Run[] runs;
        }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Time.timeScale=4;Application.targetFrameRate=120;
            Directory.CreateDirectory("Logs/Arachnid");
            var cases=NavigationWorld.Cases().Where(c=>c.split=="development").ToArray();
            var bad=cases.Single(c=>c.id=="c-b3126f2da82f0a48e35db489");
            var report=new Report {isolatingProbeRejected=!NavigationRunCheck.StillReachable(bad,new Vector2Int(9,3),new Vector2Int(9,2))};
            var runs=new List<Run>();
            foreach(var c in cases)
            {
                NavigationWorld.NextCase=c.id;NavigationWorld.NextProfile="cold";
                SceneManager.LoadScene("ECHO5_ArachnidLab");yield return null;yield return null;
                var client=FindFirstObjectByType<NavigationClient>();float ready=Time.realtimeSinceStartup+15;
                while(!client.Connected && Time.realtimeSinceStartup<ready)yield return null;
                client.Begin();float begin=Time.realtimeSinceStartup;
                bool paused=false,preserved=true;
                while(!client.SessionEnded && Time.realtimeSinceStartup-begin<180)
                {
                    if(!paused && client.Sequence>=2 && !client.Pending)
                    {
                        paused=true;int seq=client.Sequence;string token=client.Session;
                        client.Pause();yield return new WaitForSecondsRealtime(.15f);
                        preserved=client.UserPaused && !client.SessionEnded && client.Session==token && client.Sequence==seq;
                        client.ToggleRun();preserved&=client.Automatic && client.Session==token;
                    }
                    yield return null;
                }
                if(!client.SessionEnded)client.StopSession();
                float close=Time.realtimeSinceStartup+49;
                while(client.Closing && Time.realtimeSinceStartup<close)yield return null;
                var d=client.Latest;
                var run=new Run {world=c.id,session=client.Session,journal=d?.journal,reason=client.Status,
                    state=client.RunState,completed=client.TerminalCompleted,acknowledged=client.TerminalAcknowledged,
                    pending=client.TerminalPending,manualPausePreserved=paused&&preserved,
                    stopIsNotPause=client.SessionEnded&&!client.UserPaused&&client.RunButton=="Reiniciar mapa",
                    decisions=client.Sequence,moves=client.World.Moves,blocks=client.World.Blocks,
                    work=d?.budgetUsed??-1,cam=d?.basal?.camUsed??0,seconds=Time.realtimeSinceStartup-begin};
                runs.Add(run);report.runs=runs.ToArray();
                File.WriteAllText("Logs/Arachnid/pause-recovery-progress.json",JsonUtility.ToJson(report,true));
                Debug.Log("ARACHNID_RECOVERY_RUN "+JsonUtility.ToJson(run));
                if(runs.Count==1)
                {
                    string previous=client.Session;client.ToggleRun();yield return null;yield return null;
                    var next=FindFirstObjectByType<NavigationClient>();
                    report.restartAtSpawn=next && next.World.Position==new Vector2Int(c.start.x,c.start.z)&&next.Session!=previous&&next.Latest==null;
                }
            }
            report.valid=report.isolatingProbeRejected && report.restartAtSpawn && runs.Count==cases.Length &&
                runs.All(r=>r.completed && r.acknowledged && !r.pending && r.stopIsNotPause && r.manualPausePreserved);
            File.WriteAllText("Logs/Arachnid/pause-recovery-result.json",JsonUtility.ToJson(report,true));
            Time.timeScale=1;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(report.valid?0:1);
#else
            Application.Quit(report.valid?0:1);
#endif
        }
    }
}
