using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation.Arachnid
{
    // Evaluator-only runner. Geometry, truth and seed never enter the actor payload.
    public sealed class ClosureRunCheck : MonoBehaviour
    {
        [Serializable] class Run
        {
            public string world,family,session,journal,reason,terminalReceipt;
            public bool completed,acknowledged,pending,irrecoverable,arachnid;
            public int decisions,moves,blocks,work,cam,poseFrames,steppingFrames;
            public float seconds;
        }
        [Serializable] class Report {public bool valid;public string phase,profile="cold";public int required;public Run[] runs;}
        readonly List<Run> runs=new List<Run>();string output,phase;
        void Save(bool valid,int required)
        {
            File.WriteAllText(Path.Combine(output,"unity-"+phase+".json"),JsonUtility.ToJson(new Report{valid=valid,phase=phase,required=required,runs=runs.ToArray()},true));
        }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);var args=Environment.GetCommandLineArgs();
            output=args[Array.IndexOf(args,"-closureOutput")+1];phase=args[Array.IndexOf(args,"-closurePhase")+1];
            Directory.CreateDirectory(output);Time.timeScale=6;
            if(phase=="station")yield return Station();else yield return Navigation();
            int required=phase=="confirmation"?15:phase=="station"?3:8;
            int expected=phase=="confirmation"?16:required;
            bool valid=runs.Count==expected && runs.Count(r=>r.completed && r.acknowledged && !r.pending)>=required &&
                runs.All(r=>!r.irrecoverable && r.arachnid && r.poseFrames>0 && r.steppingFrames>0);
            Save(valid,required);Time.timeScale=1;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(valid?0:1);
#else
            Application.Quit(valid?0:1);
#endif
        }
        IEnumerator Navigation()
        {
            var cases=NavigationWorld.Cases().Where(c=>c.split==(phase=="confirmation"?"confirmation":"development")).ToArray();
            foreach(var c in cases)
            {
                NavigationWorld.NextCase=c.id;NavigationWorld.NextProfile="cold";
                SceneManager.LoadScene("ECHO5_ArachnidLab");yield return null;yield return null;
                var client=FindFirstObjectByType<NavigationClient>();float ready=Time.realtimeSinceStartup+15;
                while(!client.Connected && Time.realtimeSinceStartup<ready)yield return null;
                Application.targetFrameRate=120;client.Begin();float begin=Time.realtimeSinceStartup;
                while(!client.SessionEnded && Time.realtimeSinceStartup-begin<180)yield return null;
                bool timeout=!client.SessionEnded;if(timeout)client.StopSession();
                float close=Time.realtimeSinceStartup+49;while(client.Closing && Time.realtimeSinceStartup<close)yield return null;
                var d=client.Latest;var body=client.World.Robot.GetComponentInChildren<ArachnidGait>();
                var run=new Run{world=c.id,family=c.family,session=client.Session,journal=d?.journal,reason=client.Status,
                    completed=client.TerminalCompleted,acknowledged=client.TerminalAcknowledged,pending=client.TerminalPending,
                    irrecoverable=timeout || (!client.TerminalCompleted && (d==null || d.budgetRemaining>0)),
                    arachnid=body,poseFrames=body?body.PoseFrames:0,steppingFrames=body?body.SteppingFrames:0,
                    decisions=client.Sequence,moves=client.World.Moves,blocks=client.World.Blocks,
                    work=d?.budgetUsed??-1,cam=d?.basal?.camUsed??0,seconds=Time.realtimeSinceStartup-begin};
                runs.Add(run);Save(false,phase=="confirmation"?15:8);
                Debug.Log("ECHO_CLOSURE_CASE "+JsonUtility.ToJson(run));
            }
        }
        IEnumerator Station()
        {
            foreach(string mission in new[]{"irrigation","delivery","detour"})
            {
                StationClient.NextMission=mission;SceneManager.LoadScene("EchoStation");yield return null;yield return null;
                var client=FindFirstObjectByType<StationClient>();float ready=Time.realtimeSinceStartup+15;
                while(!client.Connected && Time.realtimeSinceStartup<ready)yield return null;
                Application.targetFrameRate=120;client.Begin();float begin=Time.realtimeSinceStartup;
                while(client.Automatic && Time.realtimeSinceStartup-begin<180)yield return null;
                bool timeout=client.Automatic;if(timeout)client.Pause();
                float close=Time.realtimeSinceStartup+12;while(!client.TerminalAcknowledged && Time.realtimeSinceStartup<close)yield return null;
                var body=client.World.Robot.GetComponentInChildren<ArachnidGait>();var d=client.Latest;
                var run=new Run{world=mission,family="station",session=client.Session,journal=d?.journal,reason=client.Status,
                    completed=client.World.Completed,acknowledged=client.TerminalAcknowledged,pending=!client.TerminalAcknowledged,
                    terminalReceipt=client.TerminalReceipt,irrecoverable=timeout || !client.World.Completed,
                    arachnid=body,poseFrames=body?body.PoseFrames:0,steppingFrames=body?body.SteppingFrames:0,
                    decisions=client.Sequence,work=d?.budgetUsed??-1,cam=d?.basal?.camUsed??0,seconds=Time.realtimeSinceStartup-begin};
                runs.Add(run);Save(false,3);Debug.Log("ECHO_CLOSURE_STATION "+JsonUtility.ToJson(run));
            }
        }
    }
}
