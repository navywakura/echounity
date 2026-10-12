using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoWorld
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerExplorer : MonoBehaviour
    {
        public PrototypeGame Game; public ChunkWorld World; public Camera View; public WorldHud Hud; public LocalSession Session;
        public byte SelectedBlock=3;
        public bool Building;
        public string Notice="";
        public bool WalkingPath => path != null && pathIndex < path.Count;
        public IReadOnlyList<Vector3Int> CurrentPath => path;
        public bool IsGrounded => motor && motor.isGrounded;
        public float LastFallDistance{get;private set;}public int LastFallDamage{get;private set;}
        public bool Airborne=>airborne;public float FallApex=>fallApex;public float VerticalSpeed=>vertical;
        public void RestoreFall(bool active,float apex,float speed){airborne=active;fallApex=apex;vertical=speed;supportHeight=active?apex:transform.position.y;}
        CharacterController motor; HumanoidVisual visual; List<Vector3Int> path;
        int pathIndex; float vertical, stalled, fallApex,supportHeight; bool airborne;
        DestinationMarker marker;
        public Vector3Int GlobalCell => World.ToGlobal(transform.position+Vector3.up*.08f);
        void Awake()
        {
            motor=GetComponent<CharacterController>(); motor.height=1.88f; motor.radius=.28f;
            motor.center=new Vector3(0,.94f,0); motor.stepOffset=0; motor.slopeLimit=50; motor.skinWidth=.02f;
            var body=new GameObject("Expedition suit"); body.transform.SetParent(transform,false);
            visual=body.AddComponent<HumanoidVisual>();
            marker=new GameObject("Click destination").AddComponent<DestinationMarker>();
        }
        public bool RequestMove(Vector3Int destination)
        {
            marker.World=World;
            Vector3Int start=GlobalCell;
            int startY=World.Data.WalkHeight(start.x,start.z,start.y);
            if(startY>WorldData.MinY)start.y=startY;
            path=World.Data.FindPath(start,destination,4096);pathIndex=0;stalled=0;
            bool found=path!=null&&path.Count>0;
            marker.Show(destination,found);
            Notice=found?"":"Sin ruta a pie en el presupuesto de 4.096 nodos. Los bloques se suben con salto manual.";
            return found;
        }
        public void Teleport(Vector3Int cell)
        {
            motor.enabled=false;
            World.Origin=new Vector2Int(WorldData.FloorDiv(cell.x,16)*16,WorldData.FloorDiv(cell.z,16)*16);
            transform.position=World.ToLocal(cell)+new Vector3(.5f,.05f,.5f);
            path=null;vertical=0;airborne=false;fallApex=transform.position.y;supportHeight=cell.y;
            World.Refresh();World.BuildImmediate();motor.enabled=true;
            Physics.SyncTransforms();
            if(View)View.GetComponent<IsometricCamera>()?.SnapToTarget();
        }
        public void RestorePosition(Vector3Int cell,Vector3 offset)
        {
            Teleport(cell);motor.enabled=false;transform.position=World.ToLocal(cell)+offset;
            motor.enabled=true;fallApex=transform.position.y;Physics.SyncTransforms();
            View.GetComponent<IsometricCamera>()?.SnapToTarget();
        }
        public void Respawn()
        {
            Session.RestoreVitals();
            Teleport(new Vector3Int(0,World.Data.StandHeight(0,0),0));
            Notice="Has regresado al campamento. Conservas el inventario.";
        }
        public bool Edit(Vector3Int cell, bool place)
        {
            Vector3 center=World.ToLocal(cell)+Vector3.one*.5f;
            if(!World.Data.InBounds(cell.x,cell.y,cell.z)||Vector3.Distance(center,transform.position+Vector3.up)>6)
            {Notice="Acércate: alcance de herramienta de 6 bloques.";return false;}
            byte old=World.Data.GetBlock(cell.x,cell.y,cell.z);
            if(place)
            {
                if(old!=0){Notice="Ese espacio ya está ocupado.";return false;}
                if(Game.Drops.Occupies(cell)){Notice="Recoge el material suelto antes de ocupar ese espacio.";return false;}
                if(Session.Count(SelectedBlock)<1){Notice="No queda ese material.";return false;}
                if(motor.bounds.Intersects(new Bounds(center,Vector3.one*.98f))){Notice="Ese bloque tocaría al personaje.";return false;}
            }
            else
            {
                if(old==0||cell.y==WorldData.MinY){Notice="No hay un bloque extraíble.";return false;}
                if(Game==null||!Game.Drops.HasRoom){Notice="Recoge los materiales cercanos antes de seguir.";return false;}
                if(old==10&&!Game.Chests.Empty(cell)){Notice="Vacía el cofre antes de recogerlo.";return false;}
            }
            if(!World.Data.SetBlock(cell.x,cell.y,cell.z,place?SelectedBlock:(byte)0))return false;
            if(place)
            {
                Session.Take(SelectedBlock);
                if(SelectedBlock==10)Game.Chests.InitializeEmpty(cell);
            }
            else Game.Drops.Spawn(cell,old,1);
            World.NotifyEdit(cell);Game?.Torches?.Invalidate();path=null;
            Notice=place?"":"Material suelto: acércate para recogerlo.";return true;
        }
        public void Craft()
        {
            if(Session.CraftTorches()){SelectedBlock=5;Notice="Fabricaste 4 antorchas.";}
            else Notice="Necesitas 1 madera + 1 piedra y espacio para 4 antorchas.";
        }
        public bool TryJump()
        {
            if(!motor.isGrounded||!Session.SpendJump())return false;
            vertical=7.4f;airborne=true;fallApex=transform.position.y;path=null;return true;
        }
        void Update()
        {
            if(World==null||World.Data==null||Session==null||Time.deltaTime<=0)return;
            float dt=Mathf.Min(Time.deltaTime,.08f);Vector3 horizontal=Vector3.zero;
            bool blocked=Hud&&Hud.BlocksGameplay;
            if(!blocked)
            {
                if(Input.GetKeyDown(KeyCode.B)){Building=!Building;path=null;}
                if(Input.GetKeyDown(KeyCode.C))Craft();
                for(int i=0;i<10;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0+(i+1)%10)))SelectedBlock=(byte)(i+1);
                Vector2 input=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
                if(input.sqrMagnitude>.01f)
                {
                    path=null;Vector3 right=View.transform.right,forward=View.transform.forward;right.y=forward.y=0;
                    horizontal=(right.normalized*input.x+forward.normalized*input.y).normalized;
                }
                if((!Hud||!Hud.ConsumesPointer(Input.mousePosition))&&(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1)))Pick();
                blocked=Hud&&Hud.BlocksGameplay;
                if(blocked)horizontal=Vector3.zero;
                else if(Input.GetKeyDown(KeyCode.Space))TryJump();
            }
            bool running=!blocked&&Input.GetKey(KeyCode.LeftShift)&&!Session.SprintExhausted&&Session.Stamina>1;
            float speed=running?6.8f:4.1f;
            if(!blocked&&WalkingPath)
            {
                Vector3 target=World.ToLocal(path[pathIndex])+new Vector3(.5f,0,.5f);
                Vector3 delta=target-transform.position;delta.y=0;
                if(delta.magnitude<.10f)pathIndex++;
                else horizontal=delta.normalized*Mathf.Min(1,delta.magnitude/(speed*Mathf.Max(dt,.001f)));
            }
            Vector3 next=transform.position+horizontal*.65f;Vector3Int nc=World.ToGlobal(next);
            if(!World.IsColumnReady(nc.x,nc.z))horizontal=Vector3.zero;
            if(motor.isGrounded&&vertical<0)
            {
                vertical=-2;
                // Capsule contact can remain grounded while sliding around a ledge.
                // Retain the last actual horizontal support, not the rounded capsule foot.
                if(Physics.Raycast(transform.position+Vector3.up*.15f,Vector3.down,out RaycastHit support,.4f,1,QueryTriggerInteraction.Ignore)&&support.normal.y>.9f)
                    supportHeight=support.point.y;
            }
            if(!motor.isGrounded&&!airborne){airborne=true;fallApex=Mathf.Max(transform.position.y,supportHeight);}
            if(airborne)fallApex=Mathf.Max(fallApex,transform.position.y);
            vertical-=20*dt;
            Vector3 previous=transform.position;
            CollisionFlags flags=motor.Move((horizontal*speed+Vector3.up*vertical)*dt);
            if((flags&CollisionFlags.Above)!=0&&vertical>0)vertical=0;
            bool grounded=(flags&CollisionFlags.Below)!=0;
            // Capture the last supported height before this first unsupported Move.
            if(!grounded&&!airborne){airborne=true;fallApex=Mathf.Max(previous.y,supportHeight);}
            if(grounded&&airborne&&vertical<=0)
            {
                // Below can also mean the curved capsule is rubbing the lip of a
                // ledge. Only a horizontal support below the feet ends a fall.
                if(Physics.Raycast(transform.position+Vector3.up*.15f,Vector3.down,out RaycastHit landing,.4f,1,QueryTriggerInteraction.Ignore)&&landing.normal.y>.9f)
                {
                    LastFallDistance=Mathf.Max(0,fallApex-landing.point.y);
                    int lost=Session.Land(LastFallDistance);LastFallDamage=lost;supportHeight=landing.point.y;
                    if(lost>0)Notice="Caída: -"+lost+" HP.";
                    airborne=false;vertical=-2;
                }
            }
            Vector3 actual=(transform.position-previous)/Mathf.Max(dt,.001f);actual.y=0;
            visual.SetMotion(actual,grounded,running,vertical);
            Session.StepStamina(dt,running&&actual.sqrMagnitude>1&&grounded);
            if(WalkingPath&&!blocked&&horizontal.sqrMagnitude>.1f&&actual.sqrMagnitude<.04f)stalled+=dt;else stalled=0;
            if(stalled>1){path=null;Notice="Paso bloqueado. Salta manualmente o elige otro destino.";}
            if(Session.Health<=0||transform.position.y<WorldData.MinY-2)
                Respawn();
        }
        void Pick()
        {
            View.GetComponent<IsometricCamera>()?.SynchronizeOrigin();
            RaycastHit[] hits=Physics.RaycastAll(View.ScreenPointToRay(Input.mousePosition),240,1|(1<<8),QueryTriggerInteraction.Collide);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits)
            {
                // Select the surface visible through the cutaway, keeping every solid collider alive.
                if(CutAway(hit.point))continue;
                var torch=hit.collider.GetComponent<TorchMarker>();
                Vector3Int cell=torch?torch.Cell:World.ToGlobal(hit.point-hit.normal*.015f);
                byte id=World.Data.GetBlock(cell.x,cell.y,cell.z);
                if(Input.GetMouseButtonDown(1))Edit(cell,false);
                else if(Building)Edit(World.ToGlobal(hit.point+hit.normal*.025f),true);
                else if(id==10&&Vector3.Distance(World.ToLocal(cell),transform.position)<4)Hud.OpenChest(cell);
                else RequestMove(World.ToGlobal(hit.point+hit.normal*.025f));
                break;
            }
        }
        bool CutAway(Vector3 point)
        {
            if(point.y<=transform.position.y+.2f)return false;
            Vector3 target=transform.position+Vector3.up,segment=target-View.transform.position;
            float length=segment.magnitude;Vector3 dir=segment.normalized,rel=point-View.transform.position;
            float along=Vector3.Dot(rel,dir);
            return along>.15f&&along<length-.25f&&(rel-dir*along).magnitude<1.56f;
        }
        void OnDestroy(){if(marker)Destroy(marker.gameObject);}
    }
}
