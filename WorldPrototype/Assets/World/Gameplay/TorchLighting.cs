using System.Collections.Generic;
using UnityEngine;

namespace EchoWorld
{
    public sealed class TorchMarker : MonoBehaviour {public Vector3Int Cell;}
    public sealed class TorchLighting : MonoBehaviour
    {
        public PrototypeGame Game;
        readonly Dictionary<Vector3Int,GameObject> lamps=new Dictionary<Vector3Int,GameObject>();
        Material wood,flame;float nextScan;
        public int Count=>lamps.Count;
        public int ActiveLights {get;private set;}
        void Start()
        {
            wood=HumanoidVisual.MakeMaterial("Torch handle",new Color(.25f,.13f,.05f));
            flame=HumanoidVisual.MakeMaterial("Torch flame",new Color(1,.54f,.12f),true);
        }
        public void Invalidate(){nextScan=0;}
        void Update()
        {
            if(!Game||!Game.Player||Time.timeScale==0)return;
            if(Time.time>=nextScan){Refresh();nextScan=Time.time+1;}
            foreach(var entry in lamps)
            {
                entry.Value.transform.position=Game.World.ToLocal(entry.Key)+new Vector3(.5f,0,.5f);
                Light light=entry.Value.GetComponentInChildren<Light>();
                light.intensity=2.3f+Mathf.Sin(Time.time*7+entry.Key.x)*.12f;
            }
        }
        void Refresh()
        {
            if(!wood||!flame)return;
            var center=Game.Player.GlobalCell;var data=Game.World.Data;var wanted=new HashSet<Vector3Int>();
            for(int dx=-12;dx<=12;dx++)for(int dz=-12;dz<=12;dz++)
            {
                int x=center.x+dx,z=center.z+dz;if(!Game.World.IsColumnReady(x,z))continue;
                for(int y=Mathf.Max(WorldData.MinY+1,center.y-6);y<=Mathf.Min(WorldData.MaxY-1,center.y+8);y++)
                    if(data.GetBlock(x,y,z)==5)wanted.Add(new Vector3Int(x,y,z));
            }
            var ordered=new List<Vector3Int>(wanted);ordered.Sort((a,b)=>(a-center).sqrMagnitude.CompareTo((b-center).sqrMagnitude));
            // Geometry and picking remain available beyond the nearest 24 lights.
            ActiveLights=Mathf.Min(24,ordered.Count);
            foreach(var cell in new List<Vector3Int>(lamps.Keys))if(!wanted.Contains(cell)){Destroy(lamps[cell]);lamps.Remove(cell);}
            foreach(var cell in ordered)
            {
                if(lamps.ContainsKey(cell))continue;
                var go=new GameObject("Antorcha");go.transform.position=Game.World.ToLocal(cell)+new Vector3(.5f,0,.5f);go.layer=8;
                var box=go.AddComponent<BoxCollider>();box.center=new Vector3(0,.45f,0);box.size=new Vector3(.32f,.9f,.32f);box.isTrigger=true;
                go.AddComponent<TorchMarker>().Cell=cell;
                HumanoidVisual.Part(go.transform,"Palo",new Vector3(0,.32f,0),new Vector3(.10f,.64f,.1f),wood);
                HumanoidVisual.Part(go.transform,"Llama",new Vector3(0,.74f,0),new Vector3(.19f,.28f,.19f),flame);
                var light=new GameObject("Luz").AddComponent<Light>();light.transform.SetParent(go.transform,false);light.transform.localPosition=Vector3.up*.85f;
                light.type=LightType.Point;light.range=8;light.intensity=2.3f;light.color=new Color(1,.61f,.26f);light.shadows=LightShadows.None;
                lamps.Add(cell,go);
            }
            for(int i=0;i<ordered.Count;i++)lamps[ordered[i]].GetComponentInChildren<Light>().enabled=i<24;
        }
        void OnDestroy(){foreach(var go in lamps.Values)if(go)Destroy(go);if(wood)Destroy(wood);if(flame)Destroy(flame);}
    }
}
