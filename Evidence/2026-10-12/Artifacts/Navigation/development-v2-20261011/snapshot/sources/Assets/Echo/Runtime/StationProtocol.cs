using System;
using UnityEngine;

namespace EchoStation
{
    [Serializable] public class CellData { public int x, z; public bool blocked; public CellData(int x, int z, bool blocked=false) { this.x=x;this.z=z;this.blocked=blocked; } }
    [Serializable] public class ObjectData
    {
        public string id, kind, label;
        public int x, z;
        public bool inspected, compatible, active, locked;
    }
    [Serializable] public class Observation
    {
        public string schema="echo.station.observation.v1", session, mission;
        public int seq, width=15, height=13, x,z,battery;
        public string[] inventory;
        public CellData[] cells;
        public ObjectData[] objects;
        public string lastResult;
        public bool completed;
        public ActionOutcome outcome=new ActionOutcome();
    }
    [Serializable] public class ActionOutcome
    {
        public bool present;
        public string session="",action="",objectId="",status="";
        public int seq=-1,targetX,targetZ,actualX,actualZ,distanceMm;
    }
    [Serializable] public class Decision
    {
        public string schema,session,action,objectId,reason,prediction,source,journal,phase;
        public int seq,x,z,budgetUsed,budgetRemaining;
        public string[] hypotheses,evidence;
        public CellData[] knownCells,plan;
    }
}
