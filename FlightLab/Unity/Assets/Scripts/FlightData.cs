using System;
using UnityEngine;

[Serializable] public class SceneBox {
    public string name, kind; public float[] position, size, color;
}
[Serializable] public class FlightScene {
    public int schema; public string name, variant, physics; public SceneBox[] boxes;
}
[Serializable] public class MapCell { public float x,y; public bool occupied; }
[Serializable] public class SensorRay { public float x,y; public bool hit; }
[Serializable] public class FlightFrame {
    public int schema; public float time; public string phase,arm,reason,source;
    public float[] position, rotation, estimated, target;
    public MapCell[] known; public SensorRay[] rays;
    public int inspections,skipped,visited,contacts,budget_remaining_s,px4_battery;
    public float travel_m; public double wall_unix;
}
public static class FlightCoordinates {
    public static bool Terminal(string phase) {
        return phase=="MISSION_COMPLETE" || phase=="RETURNED_PARTIAL" || phase=="CONTINGENCY_LANDED" || phase=="LANDING_INCOMPLETE" || phase=="TAKEOFF_UNCONFIRMED" || phase=="COMPLETE";
    }
    public static Vector3 Position(float[] enu) { return new Vector3(enu[0],enu[2],enu[1]); }
    public static Quaternion Attitude(float[] rpy) {
        float r=rpy[0], p=rpy[1], y=rpy[2];
        float cr=Mathf.Cos(r),sr=Mathf.Sin(r),cp=Mathf.Cos(p),sp=Mathf.Sin(p),cy=Mathf.Cos(y),sy=Mathf.Sin(y);
        return Quaternion.LookRotation(new Vector3(cy*cp,-sp,sy*cp),new Vector3(cy*sp*cr+sy*sr,cp*cr,sy*sp*cr-cy*sr));
    }
    public static bool Valid(FlightFrame f) {
        return f!=null && f.schema==1 && f.position!=null && f.position.Length==3 &&
            Array.TrueForAll(f.position,v=>!float.IsNaN(v)&&!float.IsInfinity(v)) &&
            f.rotation!=null && f.rotation.Length==3 &&
            Array.TrueForAll(f.rotation,v=>!float.IsNaN(v)&&!float.IsInfinity(v)) &&
            !float.IsNaN(f.time) && !float.IsInfinity(f.time) && f.source=="Gazebo/PX4 SITL";
    }
}
