using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoWorld
{
    [Serializable] public sealed class DropRecord
    {public Vector3Int cell;public Vector3 offset;public byte item;public int count;}
    public sealed class WorldDrops : MonoBehaviour
    {
        public const int MaxDrops=512;
        public PrototypeGame Game;
        readonly List<Entry> drops=new List<Entry>();
        readonly Dictionary<byte,Material> materials=new Dictionary<byte,Material>();
        public int ActiveCount=>drops.Count;
        public int Collected {get;private set;}
        public bool HasRoom=>drops.Count<MaxDrops;
        sealed class Entry {public DropRecord Data;public GameObject View;public float Vertical,Delay;}
        public bool Spawn(Vector3Int cell,byte item,int count=1)
        {
            if(!HasRoom||item==0||item>10||count<1||count>999)return false;
            drops.Add(new Entry{Data=new DropRecord{cell=cell,offset=Vector3.one*.5f,item=item,count=count},Delay=.45f});return true;
        }
        public bool Occupies(Vector3Int cell)
        {
            foreach(var entry in drops)
                if(new Bounds(Game.World.ToLocal(cell)+Vector3.one*.5f,Vector3.one*1.3f).Contains(Game.World.ToLocal(entry.Data.cell)+entry.Data.offset))return true;
            return false;
        }
        public DropRecord[] Snapshot()
        {var r=new DropRecord[drops.Count];for(int i=0;i<r.Length;i++)r[i]=drops[i].Data;return r;}
        public void Restore(DropRecord[] data)
        {
            foreach(var d in drops)if(d.View)Destroy(d.View);drops.Clear();
            if(data==null)return;if(data.Length>MaxDrops)throw new ArgumentException("Too many saved drops");
            foreach(var d in data)
            {
                if(d==null||d.item==0||d.item>10||d.count<1||d.count>999||!Game.World.Data.InBounds(d.cell.x,d.cell.y,d.cell.z)
                    || !Finite(d.offset.x)||!Finite(d.offset.y)||!Finite(d.offset.z)||d.offset.x<0||d.offset.x>=1||d.offset.y<0||d.offset.y>=1||d.offset.z<0||d.offset.z>=1)
                    throw new ArgumentException("Invalid drop record");
                drops.Add(new Entry{Data=d});
            }
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        Material MaterialFor(byte id)
        {
            if(!materials.TryGetValue(id,out Material m))
            {m=HumanoidVisual.MakeMaterial("Dropped "+LocalSession.Name(id),LocalSession.Color(id));materials.Add(id,m);}
            return m;
        }
        void Update(){Tick(Time.deltaTime);}
        public void Tick(float dt)
        {
            if(!Game||!Game.Player||Game.Session==null||dt<=0)return;
            var world=Game.World;Vector3 target=Game.Player.transform.position+Vector3.up*.65f;
            for(int i=drops.Count-1;i>=0;i--)
            {
                Entry e=drops[i];Vector3 position=world.ToLocal(e.Data.cell)+e.Data.offset;
                bool visible=world.IsColumnReady(e.Data.cell.x,e.Data.cell.z)&&Vector3.Distance(position,target)<48;
                if(e.View)e.View.SetActive(visible);if(!visible)continue;
                if(!e.View)
                {
                    e.View=GameObject.CreatePrimitive(PrimitiveType.Cube);e.View.name="Drop "+LocalSession.Name(e.Data.item);
                    e.View.layer=2;var c=e.View.GetComponent<Collider>();c.enabled=false;Destroy(c);
                    e.View.transform.localScale=Vector3.one*.25f;e.View.GetComponent<Renderer>().sharedMaterial=MaterialFor(e.Data.item);
                }
                e.Delay=Mathf.Max(0,e.Delay-dt);
                bool attracted=!WorldData.IsSolid(world.Data.GetBlock(e.Data.cell.x,e.Data.cell.y,e.Data.cell.z))&&e.Delay==0&&Game.Session.Count(e.Data.item)<LocalSession.StackLimit&&Vector3.Distance(position,target)<3.2f
                    && !Physics.Linecast(position,target,1,QueryTriggerInteraction.Ignore);
                if(attracted)
                {
                    position=Vector3.MoveTowards(position,target,8*dt);e.Vertical=0;
                    if(Vector3.Distance(position,target)<.4f)
                    {
                        int accepted=Game.Session.Add(e.Data.item,e.Data.count);e.Data.count-=accepted;Collected+=accepted;
                        if(e.Data.count==0){Destroy(e.View);drops.RemoveAt(i);continue;}
                    }
                }
                else
                {
                    e.Vertical-=12*dt;float descent=-e.Vertical*dt;
                    if(Physics.SphereCast(position,.13f,Vector3.down,out RaycastHit ground,descent+.04f,1,QueryTriggerInteraction.Ignore))
                    {position.y-=Mathf.Max(0,ground.distance-.02f);e.Vertical=0;}
                    else position.y-=descent;
                }
                e.Data.cell=world.ToGlobal(position);e.Data.offset=position-world.ToLocal(e.Data.cell);
                e.View.transform.position=position+Vector3.up*Mathf.Sin(Time.time*3+i)*.035f;
                e.View.transform.rotation=Quaternion.Euler(15,Time.time*70+i*23,15);
            }
        }
        void OnDestroy()
        {foreach(var d in drops)if(d.View)Destroy(d.View);foreach(var m in materials.Values)if(m)Destroy(m);}
    }
}
