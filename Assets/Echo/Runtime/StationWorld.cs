using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EchoStation
{
    public sealed class StationWorld : MonoBehaviour
    {
        public const int Width=15, Height=13;
        public const float CellSize=2f;
        public static readonly Vector2Int DetourCell=new Vector2Int(5,9);
        public Transform Robot;
        public string Mission="irrigation";
        public string LastResult="Estación iniciada. Objetivo: restaurar el riego y volver a carga.";
        public float Energy=100;
        public bool Completed {get; private set;}
        public readonly List<string> Inventory=new List<string>();
        public List<StationObject> Objects=new List<StationObject>();
        public List<Vector2Int> Walls=new List<Vector2Int>();
        public Renderer[] Tiles;
        public GameObject DetourObstacle;
        public int Actions {get; private set;}
        public ActionOutcome LastOutcome=new ActionOutcome();
        public bool IsDetourBlocked {get;private set;}
        public static Vector3 ToWorld(int x,int z) => new Vector3(x*CellSize,0,z*CellSize);
        public Vector2Int Position => new Vector2Int(Mathf.RoundToInt(Robot.position.x/CellSize),Mathf.RoundToInt(Robot.position.z/CellSize));
        public StationObject FindObject(string id) => Objects.FirstOrDefault(o=>o!=null && o.Id==id);
        public bool InBounds(Vector2Int p) => p.x>=0 && p.x<Width && p.y>=0 && p.y<Height;
        public bool Blocked(Vector2Int p)
        {
            if(!InBounds(p) || Walls.Contains(p) || (IsDetourBlocked && p==DetourCell)) return true;
            return Objects.Any(o=>o!=null && !o.Collected && o.X==p.x && o.Z==p.y &&
                (o.Kind=="door" ? o.Locked : o.Kind!="charger" && o.Kind!="delivery" && o.Kind!="key"));
        }
        public void PlaceRobot(Vector2Int p)
        {
            var cc=Robot.GetComponent<CharacterController>();
            if(cc) cc.enabled=false;
            Robot.position=ToWorld(p.x,p.y);
            if(cc) cc.enabled=true;
            Physics.SyncTransforms();
        }
        public void SetDetour(bool blocked)
        {
            if(blocked && Position==DetourCell) {LastResult="Obstáculo pospuesto: la celda está ocupada por el robot.";return;}
            IsDetourBlocked=blocked;
            if(DetourObstacle) DetourObstacle.SetActive(blocked);
            Physics.SyncTransforms();
            LastResult=blocked ? "Cambio del entorno: una caja bloquea el paso entre almacén y energía." : "Paso entre almacén y energía despejado.";
        }
        bool Visible(Vector2Int p)
        {
            var pos=Position;
            if(Mathf.Abs(p.x-pos.x)+Mathf.Abs(p.y-pos.y)>5) return false;
            // Discrete sight uses actual occluders. Endpoint itself is observable.
            var a=new Vector2(pos.x,pos.y);var b=new Vector2(p.x,p.y);
            int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)*8));
            for(int i=1;i<n;i++)
            {
                var v=Vector2.Lerp(a,b,i/(float)n);
                var q=new Vector2Int(Mathf.RoundToInt(v.x),Mathf.RoundToInt(v.y));
                if(q==p || q==pos) continue;
                if(Walls.Contains(q) || Objects.Any(o=>o.Kind=="door" && o.Locked && o.X==q.x && o.Z==q.y)) return false;
            }
            return true;
        }
        public Observation Observe(string session,int seq)
        {
            var visible=new List<CellData>();
            for(int z=0;z<Height;z++)for(int x=0;x<Width;x++)
                if(Visible(new Vector2Int(x,z))) visible.Add(new CellData(x,z,Blocked(new Vector2Int(x,z))));
            return new Observation {session=session,seq=seq,mission=Mission,width=Width,height=Height,
                x=Position.x,z=Position.y,battery=Mathf.Clamp(Mathf.CeilToInt(Energy),0,100),
                inventory=Inventory.ToArray(),cells=visible.ToArray(),
                objects=Objects.Where(o=>!o.Collected && Visible(new Vector2Int(o.X,o.Z))).Select(o=>o.PublicData()).ToArray(),
                lastResult=LastResult,completed=Completed,outcome=LastOutcome};
        }
        public void MovementResult(Vector2Int before,Vector2Int requested,bool arrived,string disposition="blocked")
        {
            Actions++;
            int distance=Mathf.Abs(Position.x-before.x)+Mathf.Abs(Position.y-before.y);
            Energy=Mathf.Max(0,Energy-0.28f*Mathf.Max(1,distance));
            LastResult=arrived && Position==requested && Vector3.Distance(Robot.position,ToWorld(requested.x,requested.y))<=.2f ? "Llegada observada a ("+requested.x+", "+requested.y+")." :
                "Movimiento "+(disposition=="cancelled"?"cancelado":"bloqueado")+"; posición observada ("+Position.x+", "+Position.y+").";
            if(Mission=="detour" && Inventory.Contains("battery") && !IsDetourBlocked) SetDetour(true);
            CheckComplete();
        }
        public void RecordOutcome(string session,int sequence,string action,string objectId,Vector2Int target,string status)
        {
            int distance=action=="move"?Mathf.CeilToInt(Vector3.Distance(Robot.position,ToWorld(target.x,target.y))*1000):0;
            if(status=="arrived" && (distance>200 || Position!=target))status="blocked";
            LastOutcome=new ActionOutcome {present=true,session=session,seq=sequence,action=action,objectId=objectId??"",status=status,
                targetX=target.x,targetZ=target.y,actualX=Position.x,actualZ=Position.y,distanceMm=distance};
        }
        public bool TryInteract(string id,bool inspect)
        {
            var o=FindObject(id);
            if(o==null || o.Collected || Mathf.Abs(o.X-Position.x)+Mathf.Abs(o.Z-Position.y)>1)
            { LastResult="Interacción rechazada: objeto ausente o fuera de alcance."; return false; }
            Actions++;Energy=Mathf.Max(0,Energy-0.12f);
            if(inspect)
            {
                o.Inspected=true;
                LastResult=o.Kind=="battery" ? (o.Compatible ? "Inspección: batería compatible con la bomba de agua." : "Inspección: batería incompatible; tensión incorrecta.") : "Inspección de "+o.Label+" registrada.";
                return true;
            }
            switch(o.Kind)
            {
                case "key": Collect(o,"key");break;
                case "parcel": Collect(o,"parcel");break;
                case "battery":
                    if(!o.Inspected) { LastResult="Batería sin inspeccionar: comprobar compatibilidad antes de recoger.";return false; }
                    if(!o.Compatible) {LastResult="Batería incompatible: se conserva en su lugar.";return false;}
                    Collect(o,"battery");break;
                case "door":
                    if(o.Locked && !Inventory.Contains("key")) {LastResult="Puerta cerrada: requiere llave de servicio.";return false;}
                    o.Locked=false;o.Active=true;
                    var collider=o.GetComponent<Collider>();if(collider) collider.enabled=false;
                    if(o.Visual) o.Visual.transform.localPosition=new Vector3(0,2.4f,0);
                    LastResult="Puerta desbloqueada; paso comprobado.";Physics.SyncTransforms();break;
                case "pump":
                    if(!o.Active && !Inventory.Contains("battery")) {LastResult="Bomba sin energía: requiere batería compatible.";return false;}
                    Inventory.Remove("battery");o.Active=true;LastResult="Bomba alimentada: indicador de presión activo.";break;
                case "valve":
                    if(!FindObject("water-pump").Active) {LastResult="Válvula sin presión: la bomba sigue apagada.";return false;}
                    o.Active=true;LastResult="Válvula abierta. El sensor confirma caudal de riego.";break;
                case "delivery":
                    if(!Inventory.Contains("parcel") && !o.Active) {LastResult="Terminal de entrega vacío.";return false;}
                    Inventory.Remove("parcel");o.Active=true;LastResult="Entrega confirmada por el terminal.";break;
                case "charger":
                    Energy=100;LastResult="Acoplamiento de carga confirmado. Batería al 100 %.";break;
                default: LastResult="Inspección disponible; no existe un actuador para este objeto.";return false;
            }
            CheckComplete();return true;
        }
        void Collect(StationObject o,string kind)
        {
            o.Collected=true;Inventory.Add(kind);
            if(o.Visual) o.Visual.SetActive(false);
            var c=o.GetComponent<Collider>();if(c)c.enabled=false;
            LastResult="Recogido: "+o.Label+". Inventario actualizado por el entorno.";
        }
        void CheckComplete()
        {
            var c=FindObject("charging-dock");
            bool returned=Mathf.Abs(Position.x-c.X)+Mathf.Abs(Position.y-c.Z)<=1;
            bool task=Mission=="delivery" ? FindObject("delivery-terminal").Active : FindObject("water-pump").Active && FindObject("water-valve").Active;
            Completed=task && returned;
            if(Completed) LastResult="Misión verificada: objetivo cumplido y regreso a carga.";
        }
        public void ShowKnowledge(CellData[] cells)
        {
            if(Tiles==null || cells==null)return;
            var known=new HashSet<int>(cells.Select(c=>c.z*Width+c.x));
            for(int i=0;i<Tiles.Length;i++) if(Tiles[i])
            {
                var block=new MaterialPropertyBlock();
                Color color=known.Contains(i)?new Color(.19f,.28f,.33f):new Color(.07f,.1f,.14f);
                block.SetColor("_Color",color);Tiles[i].SetPropertyBlock(block);
            }
        }
    }
}
