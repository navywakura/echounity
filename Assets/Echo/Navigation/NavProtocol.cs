using System;
namespace EchoStation
{
    [Serializable] public class NavRay {public int sx,sz,ex,ez; public bool hit;}
    [Serializable] public class NavOutcome
    {
        public bool present;
        public string session="",action="",status="";
        public int seq=-1,command=-1,fromX,fromZ,x,z,distanceMm;
    }
    [Serializable] public class NavObservation
    {
        public string schema="echo.navigation.observation.v1",session,world,profile;
        public int seq,width,height,x,z,goalX,goalZ,yaw;
        public bool completed;
        public NavRay[] rays;
        public NavOutcome outcome=new NavOutcome();
    }
    [Serializable] public class NavCell {public int x,z;public string state;}
    [Serializable] public class NavPoint {public int x,z;public NavPoint(int x,int z){this.x=x;this.z=z;}}
    [Serializable] public class NavCamEpisode
    {
        public int slot,sequence;
        public bool verified;
        public string label,summary,extractHash;
    }
    [Serializable] public class NavBasalState
    {
        public string schema,wsp,cause,gate,scope;
        public bool available,urgencyControl,memoryControl;
        public int[] E;
        public int camUsed,camCapacity,camDestroyed,camDropped,lastWritten,lastRead,memoryReads,memoryVetoes,urgencyScans;
        public NavCamEpisode[] episodes;
    }
    [Serializable] public class NavDecision
    {
        public string schema,session,action,reason,prediction,modelHash,journal;
        public int seq=-1,command=-1,yaw=-1,learnedActions=-1,budgetUsed=-1,budgetRemaining=-1;
        public string[] evidence,hypotheses;
        public int[] support;
        public NavCell[] cells;
        public NavPoint[] plan;
        public NavBasalState basal; // optional: original navigation endpoint remains compatible
    }
}
