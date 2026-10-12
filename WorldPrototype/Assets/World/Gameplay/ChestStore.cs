using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoWorld
{
    [Serializable] public sealed class ChestRecord
    {public Vector3Int cell;public int[] items=new int[11];}
    public sealed class ChestStore
    {
        readonly Dictionary<Vector3Int,ChestRecord> records=new Dictionary<Vector3Int,ChestRecord>();
        readonly PrototypeGame game;
        public ChestStore(PrototypeGame owner){game=owner;}
        public void InitializeEmpty(Vector3Int cell)
        {records[cell]=new ChestRecord{cell=cell};}
        ChestRecord Get(Vector3Int cell)
        {
            if(!records.TryGetValue(cell,out ChestRecord record))
            {
                record=new ChestRecord{cell=cell};record.items[2]=6;record.items[5]=4;record.items[6]=8;
                records.Add(cell,record);
            }
            return record;
        }
        bool Reachable(Vector3Int cell)=>game.World.Data.GetBlock(cell.x,cell.y,cell.z)==10 &&
            Vector3.Distance(game.World.ToLocal(cell)+Vector3.one*.5f,game.Player.transform.position+Vector3.up)<4;
        public int Count(Vector3Int cell,byte id)=>id>0&&id<=10?Get(cell).items[id]:0;
        public bool TakeOne(Vector3Int cell,byte id)
        {
            if(id==0||id>10||!Reachable(cell))return false;
            ChestRecord r=Get(cell);if(r.items[id]==0||game.Session.Add(id)!=1)return false;
            r.items[id]--;return true;
        }
        public bool DepositOne(Vector3Int cell,byte id)
        {
            if(id==0||id>10||!Reachable(cell))return false;
            ChestRecord r=Get(cell);if(r.items[id]>=LocalSession.StackLimit||!game.Session.Take(id))return false;
            r.items[id]++;return true;
        }
        public bool Empty(Vector3Int cell)
        {foreach(int n in Get(cell).items)if(n>0)return false;return true;}
        public ChestRecord[] Snapshot()=>new List<ChestRecord>(records.Values).ToArray();
        public void Restore(ChestRecord[] saved)
        {
            records.Clear();if(saved==null)return;
            foreach(var r in saved)
            {
                if(r==null||r.items==null||r.items.Length!=11||records.ContainsKey(r.cell))throw new ArgumentException("Invalid chest state");
                for(int i=0;i<11;i++)if(r.items[i]<0||r.items[i]>999||i==0&&r.items[i]!=0)throw new ArgumentException("Invalid chest item");
                records.Add(r.cell,r);
            }
        }
    }
}
