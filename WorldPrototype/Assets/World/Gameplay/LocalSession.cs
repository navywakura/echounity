using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EchoWorld
{
    // A local gameplay session. No network transport or ECHO policy is implied.
    public sealed class LocalSession
    {
        public const int MaxChatLength = 140;
        public const int SlotCount=10, MaxHealth=20, MaxStamina=100, StackLimit=999;
        readonly int[] inventory = { 0, 24, 24, 24, 12, 8, 24, 8, 8, 8, 2 };
        public int Health { get; private set; }=MaxHealth;
        public float Stamina { get; private set; }=MaxStamina;
        public int Level=>1;
        public int Xp=>0;
        public bool SprintExhausted { get; private set; }
        public readonly Queue<string> ChatHistory = new Queue<string>();
        public string LastMessage { get; private set; } = "";
        public float MessageExpires { get; private set; }
        public int Count(byte block) => block > 0 && block < inventory.Length ? inventory[block] : 0;
        public bool Take(byte block,int amount=1)
        {
            if (amount<=0 || Count(block)<amount) return false;
            inventory[block]-=amount; return true;
        }
        public void Collect(byte block)
        {
            Add(block);
        }
        public int Add(byte block,int amount=1)
        {
            if(block==0||block>=inventory.Length||amount<=0)return 0;
            int accepted=Math.Min(amount,StackLimit-inventory[block]);inventory[block]+=accepted;return accepted;
        }
        public bool CraftBeacon()
        {
            return CraftTorches();
        }
        public bool CraftTorches()
        {
            if(Count(6)<1||Count(2)<1||Count(5)>StackLimit-4)return false;
            inventory[6]--;inventory[2]--;inventory[5]+=4;return true;
        }
        public static int FallDamage(float blocks)
        {
            if(float.IsNaN(blocks)||blocks<5)return 0;
            if(blocks>=14)return MaxHealth;
            return Math.Min(MaxHealth,Math.Max(0,((int)Math.Floor(blocks)-4)*2));
        }
        public int Land(float blocks)
        {
            int damage=Math.Min(Health,FallDamage(blocks));Health-=damage;return damage;
        }
        public void Damage(int amount){Health=Math.Max(0,Health-Math.Max(0,amount));}
        public bool SpendJump()
        {if(Stamina<12||Health==0)return false;Stamina-=12;return true;}
        public void StepStamina(float dt,bool running)
        {
            if(dt<0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            Stamina=Mathf.Clamp(Stamina+(running?-18:15)*dt,0,MaxStamina);
            if(Stamina<=1)SprintExhausted=true;
            else if(Stamina>=25)SprintExhausted=false;
        }
        public void RestoreVitals(){Health=MaxHealth;Stamina=MaxStamina;SprintExhausted=false;}
        public int[] SnapshotInventory(){return (int[])inventory.Clone();}
        public void Restore(int[] items,int health,float stamina)
        {
            if(items==null||items.Length!=inventory.Length)throw new ArgumentException("Invalid inventory length");
            for(int i=0;i<items.Length;i++)if(items[i]<0||items[i]>StackLimit)throw new ArgumentException("Invalid item stack");
            if(items[0]!=0||health<0||health>MaxHealth||float.IsNaN(stamina)||stamina<0||stamina>MaxStamina)throw new ArgumentException("Invalid session state");
            Array.Copy(items,inventory,inventory.Length);Health=health;Stamina=stamina;SprintExhausted=stamina<=1;
        }
        public bool Say(string message, float now)
        {
            string clean = CleanChat(message);
            if (clean.Length == 0) return false;
            LastMessage = clean; MessageExpires = now + 8;
            ChatHistory.Enqueue(clean);
            while (ChatHistory.Count > 4) ChatHistory.Dequeue();
            return true;
        }
        public static string CleanChat(string input)
        {
            var result = new StringBuilder();
            foreach (char c in input ?? "")
            {
                if (result.Length >= MaxChatLength) break;
                if (!char.IsControl(c) && !char.IsSurrogate(c)) result.Append(c);
            }
            return result.ToString().Trim();
        }
        public static string Name(byte id)
        {
            switch (id) { case 1:return "Tierra"; case 2:return "Piedra";
                case 3:return "Aleación"; case 4:return "Vidrio"; case 5:return "Antorcha";
                case 6:return "Madera"; case 7:return "Hojas"; case 8:return "Arena";
                case 9:return "Nieve";case 10:return "Cofre";default:return "Aire"; }
        }
        public static Color Color(byte id)
        {
            switch (id) { case 1:return new Color(.42f,.53f,.25f); case 2:return new Color(.40f,.42f,.43f);
                case 3:return new Color(.65f,.77f,.81f); case 4:return new Color(.13f,.91f,.89f);
                case 6:return new Color(.48f,.28f,.12f);case 7:return new Color(.24f,.49f,.17f);
                case 8:return new Color(.81f,.70f,.45f);case 9:return new Color(.86f,.91f,.96f);
                case 10:return new Color(.68f,.40f,.14f);default:return new Color(1,.64f,.25f); }
        }
    }
}
