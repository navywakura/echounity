using System;
using UnityEngine;

namespace EchoStation
{
    // Read-only presentation of sensor pixels and actual decision receipts.
    public sealed class NavigationHUD : MonoBehaviour
    {
        public NavigationClient Client;
        readonly Color ink=new Color(.045f,.073f,.105f),panel=new Color(.064f,.1f,.14f),dim=new Color(.54f,.65f,.73f),teal=new Color(.24f,.9f,.78f);
        GUIStyle small,body,title,number,button;
        float width;
        int lastWidth,lastHeight;
        void Styles()
        {
            if(body!=null)return;
            small=new GUIStyle(GUI.skin.label){fontSize=13,wordWrap=true};small.normal.textColor=dim;
            body=new GUIStyle(GUI.skin.label){fontSize=16,wordWrap=true};body.normal.textColor=new Color(.86f,.92f,.96f);
            title=new GUIStyle(body){fontSize=25,fontStyle=FontStyle.Bold};
            number=new GUIStyle(title){fontSize=28};number.normal.textColor=teal;
            button=new GUIStyle(GUI.skin.button){fontSize=14,wordWrap=true};
        }
        void Box(Rect r,Color color){var old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void Text(float x,float y,float w,float h,string text,GUIStyle style=null)=>GUI.Label(new Rect(x,y,w,h),text,style??body);
        void Rule(float y,float x,float w)=>Box(new Rect(x,y,w,1),new Color(.15f,.23f,.29f));
        void OnGUI()
        {
            if(!Client || !Client.World || Client.World.Case==null)return;
            Styles();float scale=Mathf.Min(Screen.width/1640f,Screen.height/980f);width=Screen.width/scale;
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));float h=Screen.height/scale;
            float side=width-410,main=side-24;
            float foot=Mathf.Max(720,h-260),offset=foot-720;
            if(Camera.main)
            {
                Camera.main.rect=new Rect(0,(h-foot)/h,side/width,(foot-80)/h);
                if(lastWidth!=Screen.width || lastHeight!=Screen.height)
                {
                    float halfWidth=(Client.World.Case.width+Client.World.Case.height-2)*.707107f;
                    Camera.main.orthographicSize=Mathf.Max(halfWidth/Camera.main.aspect,halfWidth*.57735f+2)*1.12f;
                    lastWidth=Screen.width;lastHeight=Screen.height;
                }
            }
            Box(new Rect(0,0,width,80),ink);Box(new Rect(side,80,410,h-80),panel);
            Text(24,17,420,40,"ECHO / NAVEGACIÓN",title);
            Text(430,20,main-460,42,"Cámara RGB-D · mapa parcial · modelo motor aprendido",small);
            Text(side+20,17,360,40,Client.RunState,body);
            Text(24,90,main-30,42,"VISTA DEL EVALUADOR  /  el robot no recibe este mapa completo",small);
            var d=Client.Latest;var world=Client.World;
            float x=side+22,w=366;
            Text(x,100,w,26,"DECISIONES REGISTRADAS",small);
            Text(x,133,w,89,Client.Status,body);
            Rule(229,x,w);Text(x,240,w,24,"PREDICCIÓN",small);
            Text(x,267,w,74,d?.prediction??"Aún no hay una decisión del agente.");
            Text(x,350,w,24,"CONSECUENCIA OBSERVADA",small);
            var outcome=world.LastOutcome;
            Text(x,377,w,64,outcome.present?$"{outcome.action} / {outcome.status} · ({outcome.fromX},{outcome.fromZ}) → ({outcome.x},{outcome.z})":"Sin movimiento ejecutado en esta sesión.");
            Rule(450,x,w);Text(x,461,w,24,"MODELO Y EVIDENCIA",small);
            Text(x,488,w,118,d==null?"Las hipótesis aparecerán después de observar y actuar.":string.Join("\n",d.hypotheses??Array.Empty<string>()),small);
            Text(x,610,w,82,d==null?"":string.Join("\n",d.evidence??Array.Empty<string>()),small);
            Rule(700,x,w);Text(x,711,175,25,"COMANDOS APRENDIDOS",small);
            Text(x,740,170,43,d==null?"—":d.learnedActions+" / 4",number);
            Text(x+184,711,180,25,"DECISIONES",small);Text(x+184,740,175,43,Client.Sequence+" / 320",number);
            Text(x,793,w,40,d==null?"Presupuesto aún no asignado":$"Trabajo: {d.budgetUsed:N0} · restante: {d.budgetRemaining:N0}",small);
            Text(x,839,w,57,$"{world.Case.id}  ·  {Client.Profile}\n{world.Moves} movimientos · {world.Scans} giros · {world.Blocks} bloqueos",small);
            Text(x,909,w,42,"RGB-D y odometría simulados. El RGB se muestra; el agente utiliza la profundidad.",small);
            GUI.BeginGroup(new Rect(0,offset,side,h-offset));
            Box(new Rect(0,720,side,h-720),ink);
            Text(24,730,365,23,"CÁMARA DEL ROBOT  /  PRIMERA PERSONA",small);
            if(world.Sensor.Image)GUI.DrawTexture(new Rect(24,760,320,180),world.Sensor.Image,ScaleMode.ScaleToFit,false);
            else Text(24,800,320,90,"Esperando imagen de la cámara…",small);
            Text(362,730,220,23,"MEMORIA DEL MAPA",small);Map(new Rect(362,761,182,182),d);
            Text(560,760,main-574,45,"Oscuro: desconocido · azul: libre\nRojo: ocupado · ámbar: inferido",small);
            Text(560,812,main-574,30,$"Posición ({world.Position.x},{world.Position.y})  →  meta indicada ({world.Case.goal.x},{world.Case.goal.z})",small);
            GUI.enabled=!Client.Closing;
            if(GUI.Button(new Rect(560,850,145,36),Client.RunButton,button))Client.ToggleRun();
            GUI.enabled=true;
            if(GUI.Button(new Rect(717,850,145,36),"Adquirir modelo",button))Client.Train();
            if(GUI.Button(new Rect(874,850,145,36),"Otro mundo",button))Client.NextMap();
            if(GUI.Button(new Rect(560,896,145,36),"Repetir / memoria",button))Client.Load(world.Case.id,"remembered");
            if(GUI.Button(new Rect(717,896,145,36),"Desde cero",button))Client.Load(world.Case.id,"cold");
            if(GUI.Button(new Rect(874,896,145,36),"Añadir obstáculo",button))Client.PlaceObstacle();
            Text(24,950,main,24,"ESPACIO iniciar/pausar · V predicción física · B obstáculo · F seguir · rueda zoom · Q/E cámara en pausa",small);
            GUI.EndGroup();
            GUI.matrix=Matrix4x4.identity;
        }
        void Map(Rect area,NavDecision d)
        {
            var world=Client.World;float cell=Mathf.Min(area.width/world.Case.width,area.height/world.Case.height);
            Box(area,new Color(.023f,.038f,.055f));
            if(d?.cells!=null)foreach(var p in d.cells)
            {
                if(p.x<0 || p.z<0 || p.x>=world.Case.width || p.z>=world.Case.height)continue;
                Color c=p.state=="occupied"?new Color(.74f,.35f,.28f):p.state=="inferred"?new Color(.74f,.55f,.2f):new Color(.19f,.35f,.43f);
                Box(new Rect(area.x+p.x*cell,area.y+(world.Case.height-1-p.z)*cell,cell-1,cell-1),c);
            }
            if(d?.plan!=null)foreach(var p in d.plan)
                Box(new Rect(area.x+(p.x+.38f)*cell,area.y+(world.Case.height-1-p.z+.38f)*cell,cell*.24f,cell*.24f),teal);
            var goal=world.Case.goal;Box(new Rect(area.x+(goal.x+.25f)*cell,area.y+(world.Case.height-1-goal.z+.25f)*cell,cell*.5f,cell*.5f),new Color(.48f,.94f,.46f));
            var pos=world.Position;Box(new Rect(area.x+(pos.x+.15f)*cell,area.y+(world.Case.height-1-pos.y+.15f)*cell,cell*.7f,cell*.7f),Color.white);
        }
    }
}
