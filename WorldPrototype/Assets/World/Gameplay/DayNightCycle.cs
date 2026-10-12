using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoWorld
{
    public sealed class DayNightCycle : MonoBehaviour
    {
        public const double DaySeconds=900,NightSeconds=900,CycleSeconds=1800;
        public double Elapsed {get;private set;}=225;
        public bool IsDay=>Phase(Elapsed)<DaySeconds;
        public string TimeLabel=>IsDay?"Día":"Noche";
        public Light Sun;
        public Camera View;
        public static double Phase(double time)=>((time%CycleSeconds)+CycleSeconds)%CycleSeconds;
        public void SetTime(double value)
        {if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("Invalid clock");Elapsed=Math.Max(0,value);Apply();}
        void Update(){Elapsed+=Time.deltaTime;Apply();}
        void Apply()
        {
            float phase=(float)(Phase(Elapsed)/CycleSeconds);
            float altitude=Mathf.Sin(phase*Mathf.PI*2);
            float daylight=Mathf.SmoothStep(0,1,Mathf.Clamp01(altitude*3));
            if(Sun)
            {
                Sun.transform.rotation=Quaternion.Euler(phase*360, -35,0);
                Sun.color=Color.Lerp(new Color(.95f,.51f,.28f),new Color(1,.96f,.84f),daylight);
                Sun.intensity=.04f+daylight*.96f;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=Color.Lerp(new Color(.035f,.05f,.09f),new Color(.40f,.46f,.52f),daylight);
            Shader.SetGlobalFloat("_EchoDaylight",daylight);
            if(View)View.backgroundColor=Color.Lerp(new Color(.012f,.026f,.065f),new Color(.37f,.56f,.70f),daylight);
        }
    }
}
