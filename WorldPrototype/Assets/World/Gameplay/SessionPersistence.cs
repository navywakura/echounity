using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace EchoWorld
{
    [Serializable] public sealed class SessionRecord
    {
        public int version=2,seed,health=20;
        public float stamina=100;
        public int[] inventory;
        public Vector3Int cell; public Vector3 offset;
        public double clock;
        public bool airborne; public float fallApex,vertical;
        public ChestRecord[] chests; public DropRecord[] drops;
    }
    public static class SessionPersistence
    {
        const int MaxBytes=4*1024*1024;
        public static void Validate(SessionRecord r,int seed)
        {
            if(r==null||r.version!=2||r.seed!=seed)throw new InvalidDataException("Session version/seed mismatch");
            new LocalSession().Restore(r.inventory,r.health,r.stamina);
            if(float.IsNaN(r.fallApex)||float.IsInfinity(r.fallApex)||float.IsNaN(r.vertical)||float.IsInfinity(r.vertical)||Math.Abs(r.vertical)>500||r.fallApex<WorldData.MinY-2||r.fallApex>WorldData.MaxY+10)
                throw new InvalidDataException("Invalid saved motion");
            if(!Position(r.cell,r.offset)||double.IsNaN(r.clock)||double.IsInfinity(r.clock)||r.clock<0)
                throw new InvalidDataException("Invalid saved position or clock");
            if(r.drops==null||r.chests==null||r.drops.Length>WorldDrops.MaxDrops||r.chests.Length>8192)
                throw new InvalidDataException("Invalid saved object count");
            foreach(var d in r.drops)
                if(d==null||!Position(d.cell,d.offset)||d.item==0||d.item>10||d.count<1||d.count>999)
                    throw new InvalidDataException("Invalid saved drop");
            var cells=new System.Collections.Generic.HashSet<Vector3Int>();
            foreach(var c in r.chests)
            {
                if(c==null||c.items==null||c.items.Length!=11||!Position(c.cell,Vector3.zero)||!cells.Add(c.cell))
                    throw new InvalidDataException("Invalid saved chest");
                for(int i=0;i<11;i++)if(c.items[i]<0||c.items[i]>999||(i==0&&c.items[i]!=0))throw new InvalidDataException("Invalid chest contents");
            }
        }
        static bool Fraction(float v)=>!float.IsNaN(v)&&v>=0&&v<1;
        static bool Position(Vector3Int cell,Vector3 offset)=>cell.x>=WorldData.WorldMin&&cell.x<WorldData.WorldMax&&
            cell.z>=WorldData.WorldMin&&cell.z<WorldData.WorldMax&&cell.y>=WorldData.MinY&&cell.y<WorldData.MaxY&&
            Fraction(offset.x)&&Fraction(offset.y)&&Fraction(offset.z);
        public static SessionRecord Read(string root,int seed)
        {
            string file=Path.Combine(root,"session-v2.json");if(!File.Exists(file))return null;
            if(new FileInfo(file).Length>MaxBytes)throw new InvalidDataException("Session file too large");
            var r=JsonUtility.FromJson<SessionRecord>(File.ReadAllText(file));Validate(r,seed);return r;
        }
        public static void Write(string root,SessionRecord record)
        {
            Validate(record,record.seed);Directory.CreateDirectory(root);
            string target=Path.Combine(root,"session-v2.json"),tmp=target+".tmp";
            byte[] bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(record,true));
            if(bytes.Length>MaxBytes)throw new InvalidDataException("Session file too large");
            using(var file=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None))
            {file.Write(bytes,0,bytes.Length);file.Flush(true);}
            if(File.Exists(target))File.Replace(tmp,target,target+".bak");else File.Move(tmp,target);
        }
    }
}
