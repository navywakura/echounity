using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EchoStation.Arachnid
{
    /// <summary>Presentation IK driven by actual root displacement. Never chooses navigation actions.</summary>
    [DisallowMultipleComponent]
    public sealed class ArachnidGait : MonoBehaviour
    {
        sealed class Limb
        {
            public Transform coxa, femur, tibia, foot;
            public Quaternion coxaRest, femurRest, tibiaRest, footRest;
            public Vector3 rest, anchor, from, target;
            public float upper, lower, phase, lift;
            public int group;
            public bool stepping;
        }
        readonly List<Limb> limbs=new List<Limb>();
        Transform mover,body;
        Vector3 lastPosition,velocity,bodyRest;
        int nextGroup;
        public int PlantedFeet {get;private set;}=8;
        public float MeasuredSpeed {get;private set;}
        public bool Ready => limbs.Count==8;
        public int PoseFrames {get;private set;}
        public int SteppingFrames {get;private set;}
        public int MinimumPlantedFeet {get;private set;}=8;
        public bool FinitePose {get;private set;}=true;
        public Color SensorColor=new Color(0,.898f,1);
        MaterialPropertyBlock emission;
        readonly List<Renderer> eyes=new List<Renderer>();
        static Transform Named(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);

        void Start()
        {
            emission=new MaterialPropertyBlock();
            mover=GetComponentInParent<PredictiveMotor>()?.transform ?? transform;
            lastPosition=mover.position;
            body=Named(transform,"Body_Pivot");if(body)bodyRest=body.localPosition;
            foreach(string side in new[]{"L","R"})for(int i=0;i<4;i++)
            {
                string prefix="Leg_"+side+i;
                var l=new Limb{coxa=Named(transform,prefix+"_Coxa"),femur=Named(transform,prefix+"_Femur"),
                    tibia=Named(transform,prefix+"_Tibia"),foot=Named(transform,prefix+"_Foot"),group=(i+(side=="L"?0:1))%2};
                if(!l.coxa || !l.femur || !l.tibia || !l.foot)throw new InvalidOperationException("Missing articulated pivot: "+prefix);
                l.coxaRest=l.coxa.localRotation;l.femurRest=l.femur.localRotation;l.tibiaRest=l.tibia.localRotation;l.footRest=l.foot.localRotation;
                l.rest=mover.InverseTransformPoint(l.foot.position);l.anchor=l.foot.position;
                l.lift=l.rest.y;l.upper=Vector3.Distance(l.femur.position,l.tibia.position);l.lower=Vector3.Distance(l.tibia.position,l.foot.position);
                limbs.Add(l);
            }
            foreach(var renderer in GetComponentsInChildren<Renderer>())
                if(renderer.sharedMaterials.Any(m=>m && m.name.StartsWith("Mat_ECHO_Eye_Emission")))eyes.Add(renderer);
        }
        Vector3 Ground(Vector3 desired,float offset)
        {
            if(Physics.Raycast(desired+Vector3.up,Vector3.down,out var hit,2f,1<<9,QueryTriggerInteraction.Ignore))
                desired.y=hit.point.y+offset;
            return desired;
        }
        void LateUpdate()
        {
            if(!Ready || !mover)return;
            float dt=Mathf.Max(Time.deltaTime,.001f);
            Vector3 delta=mover.position-lastPosition;lastPosition=mover.position;
            if(delta.magnitude>1.5f){foreach(var l in limbs){l.anchor=mover.TransformPoint(l.rest);l.stepping=false;}}
            velocity=Vector3.Lerp(velocity,delta/dt,1-Mathf.Exp(-12*dt));MeasuredSpeed=velocity.magnitude;
            bool swinging=limbs.Any(l=>l.stepping);
            if(!swinging)
            {
                float largest=0;int group=nextGroup;
                foreach(var l in limbs)
                {
                    float error=Vector3.Distance(l.anchor,mover.TransformPoint(l.rest));
                    if(error>largest){largest=error;group=l.group;}
                }
                if(largest>.115f)
                {
                    foreach(var l in limbs.Where(l=>l.group==group))
                    {
                        l.from=l.anchor;l.target=Ground(mover.TransformPoint(l.rest)+Vector3.ClampMagnitude(velocity*.09f,.14f),l.lift);
                        l.phase=0;l.stepping=true;
                    }
                    nextGroup=1-group;
                }
            }
            PlantedFeet=0;
            foreach(var l in limbs)
            {
                if(l.stepping)
                {
                    l.phase=Mathf.Min(1,l.phase+dt/.16f);
                    l.anchor=Vector3.Lerp(l.from,l.target,Mathf.SmoothStep(0,1,l.phase))+Vector3.up*(Mathf.Sin(l.phase*Mathf.PI)*.08f);
                    if(l.phase>=1)l.stepping=false;
                }
                if(!l.stepping)PlantedFeet++;
                Pose(l,l.anchor);
                Vector3 p=l.foot.position;
                FinitePose &= !float.IsNaN(p.x+p.y+p.z) && !float.IsInfinity(p.x+p.y+p.z);
            }
            PoseFrames++;
            if(PlantedFeet<8)SteppingFrames++;
            MinimumPlantedFeet=Mathf.Min(MinimumPlantedFeet,PlantedFeet);
            if(body)body.localPosition=bodyRest+Vector3.up*(Mathf.Sin(Time.time*6)*.004f*Mathf.Min(MeasuredSpeed,1));
            for(int i=0;i<eyes.Count;i++)
            {
                var materials=eyes[i].sharedMaterials;
                for(int slot=0;slot<materials.Length;slot++)if(materials[slot] && materials[slot].name.StartsWith("Mat_ECHO_Eye_Emission"))
                {emission.Clear();emission.SetColor("_EmissionColor",SensorColor*5);emission.SetColor("_Color",SensorColor);eyes[i].SetPropertyBlock(emission,slot);}
            }
        }
        void Pose(Limb l,Vector3 target)
        {
            l.coxa.localRotation=l.coxaRest;l.femur.localRotation=l.femurRest;l.tibia.localRotation=l.tibiaRest;l.foot.localRotation=l.footRest;
            Vector3 restDirection=Vector3.ProjectOnPlane(l.foot.position-l.coxa.position,mover.up);
            Vector3 direction=Vector3.ProjectOnPlane(target-l.coxa.position,mover.up);
            float yaw=Mathf.Clamp(Vector3.SignedAngle(restDirection,direction,mover.up),-25,25);
            l.coxa.rotation=Quaternion.AngleAxis(yaw,mover.up)*l.coxa.rotation;
            Vector3 hip=l.femur.position,to=target-hip;
            float distance=Mathf.Clamp(to.magnitude,Mathf.Abs(l.lower-l.upper)+.001f,l.lower+l.upper-.001f);
            Vector3 axis=to.normalized;
            float along=(l.upper*l.upper-l.lower*l.lower+distance*distance)/(2*distance);
            float height=Mathf.Sqrt(Mathf.Max(0,l.upper*l.upper-along*along));
            Vector3 pole=Vector3.ProjectOnPlane(mover.up,axis).normalized;
            if(pole.sqrMagnitude<.001f)pole=mover.right;
            Vector3 knee=hip+axis*along+pole*height;
            l.femur.rotation=Quaternion.FromToRotation(l.tibia.position-hip,knee-hip)*l.femur.rotation;
            l.tibia.rotation=Quaternion.FromToRotation(l.foot.position-l.tibia.position,target-l.tibia.position)*l.tibia.rotation;
            l.foot.rotation=Quaternion.FromToRotation(l.foot.up,mover.up)*l.foot.rotation;
        }
    }
}
