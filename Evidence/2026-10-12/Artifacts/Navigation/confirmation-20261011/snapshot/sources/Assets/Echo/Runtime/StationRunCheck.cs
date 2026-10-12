using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoStation
{
    // Optional integration runner, activated only by an explicit -stationCheck.
    // Records real controller/physics/HTTP runs. Never credits a proposed action.
    public sealed class StationRunCheck : MonoBehaviour
    {
        [Serializable] class Run {public string mission,session,status,result;public bool passed,obstacleObserved,usedAlternativeRoute,crossedBlockedCell;public int actions,decisions;public float battery,seconds;}
        [Serializable] class Receipt {public bool valid;public string scope="Unity + ECHO external planner, three development missions";public Run[] runs;}
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);Time.timeScale=6;Application.targetFrameRate=120;
            var runs=new List<Run>();
            foreach(string mission in new[]{"irrigation","delivery","detour"})
            {
                StationClient.NextMission=mission;
                SceneManager.LoadScene("EchoStation");yield return null;yield return null;
                var client=FindFirstObjectByType<StationClient>();
                yield return new WaitForSecondsRealtime(.5f);
                client.Begin();float begin=Time.realtimeSinceStartup;
                bool observed=false,alternative=false,crossed=false;
                while(client.Automatic && Time.realtimeSinceStartup-begin<180)
                {
                    if(mission=="detour" && client.World.IsDetourBlocked)
                    {
                        var p=client.World.Position;
                        crossed|=p==StationWorld.DetourCell;
                        alternative|=p==new Vector2Int(5,3) || p==new Vector2Int(7,6);
                        if(client.Latest?.knownCells!=null)
                            observed|=Array.Exists(client.Latest.knownCells,c=>c.x==5 && c.z==9 && c.blocked);
                    }
                    yield return null;
                }
                var run=new Run {mission=mission,session=client.Session,passed=client.World.Completed && (mission!="detour" || (observed && alternative && !crossed)),
                    obstacleObserved=observed,usedAlternativeRoute=alternative,crossedBlockedCell=crossed,
                    status=client.Status,result=client.World.LastResult,actions=client.World.Actions,
                    decisions=client.Sequence,battery=client.World.Energy,seconds=Time.realtimeSinceStartup-begin};
                runs.Add(run);client.Pause();
                // Let the terminal HTTP receipt finish before scene teardown or editor exit.
                yield return new WaitForSecondsRealtime(.4f);
                Debug.Log("STATION_INTEGRATION "+JsonUtility.ToJson(run));
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/integration-progress.json",JsonUtility.ToJson(new Receipt{valid=false,runs=runs.ToArray()},true));
                if(!run.passed)break;
            }
            bool passed=runs.Count==3 && runs.TrueForAll(r=>r.passed);
            File.WriteAllText("Logs/integration-tests.json",JsonUtility.ToJson(new Receipt{valid=passed,runs=runs.ToArray()},true));
            Time.timeScale=1;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed?0:1);
#else
            Application.Quit(passed?0:1);
#endif
        }
    }
}
