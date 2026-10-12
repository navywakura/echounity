using System;
using System.Linq;
using UnityEngine;

namespace EchoStation.Arachnid
{
    /// <summary>Read-only receipts, measured observer frames and actual Unity cameras.</summary>
    public sealed class ArachnidHUD : MonoBehaviour
    {
        public NavigationClient Client;
        ArachnidDemo demo;
        GUIStyle text,small,heading,large,button;
        readonly Color bg=new Color(.045f,.070f,.092f),panel=new Color(.067f,.10f,.13f),cyan=new Color(0,.9f,1),muted=new Color(.51f,.64f,.7f);
        bool initialized;
        bool showBasal=true;
        int lastW,lastH;
        void Styles()
        {
            if(initialized)return;initialized=true;
            text=new GUIStyle(GUI.skin.label){fontSize=16,wordWrap=true};text.normal.textColor=new Color(.87f,.91f,.94f);
            small=new GUIStyle(text){fontSize=12};small.normal.textColor=muted;
            heading=new GUIStyle(text){fontSize=23,fontStyle=FontStyle.Bold};large=new GUIStyle(heading){fontSize=30};
            button=new GUIStyle(GUI.skin.button){fontSize=13,padding=new RectOffset(5,5,7,7)};
        }
        static void Box(Rect r,Color c){Color old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void Label(float x,float y,float w,float h,string message,GUIStyle style=null)=>GUI.Label(new Rect(x,y,w,h),message,style??text);
        void OnGUI()
        {
            if(!Client || Client.World==null || Client.World.Case==null)return;
            Styles();if(!demo)demo=GetComponent<ArachnidDemo>();
            float scale=Mathf.Min(Screen.width/1640f,Screen.height/1000f),w=Screen.width/scale,h=Screen.height/scale,side=w-395;
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            Box(new Rect(0,0,w,74),bg);Box(new Rect(side,74,395,h-74),panel);Box(new Rect(0,h-265,side,265),bg);
            Label(23,10,600,38,"ECHO-5 / ARÁCNIDO",large);
            Label(25,49,900,20,"Cuerpo original · ocho patas IK · profundidad y odometría · CPU local",small);
            Label(side+23,22,350,42,Client.RunState,heading);
            float viewHeight=h-339;
            if(Camera.main)
            {
                Camera.main.rect=new Rect(0,265/h,side/w,viewHeight/h);
                if(lastW!=Screen.width || lastH!=Screen.height)
                {
                    float extent=(Client.World.Case.width+Client.World.Case.height-2)*.7071f;
                    Camera.main.orthographicSize=Mathf.Max(extent/Camera.main.aspect,extent*.57735f+2)*1.08f;
                    lastW=Screen.width;lastH=Screen.height;
                }
            }
            Label(23,85,side-46,24,"MUNDO DEL EVALUADOR  ·  ECHO recibe solo lo observado",small);
            float x=side+22,iw=351;
            var d=Client.Latest;var world=Client.World;
            if(GUI.Button(new Rect(x,86,172,28),"E[6] + CAM",button))showBasal=true;
            if(GUI.Button(new Rect(x+179,86,172,28),"Decisión + LIF",button))showBasal=false;
            if(showBasal)Basal(x,130,iw,d?.basal);
            else {
            Label(x,124,iw,38,d==null?"Preparando sensores":d.action.ToUpperInvariant(),heading);
            Label(x,169,iw,68,d?.prediction??Client.Status);
            Label(x,245,iw,20,"CONSECUENCIA REGISTRADA",small);
            Label(x,273,iw,49,world.LastOutcome.present?world.LastOutcome.action+" / "+world.LastOutcome.status:"Aún no hay una acción completada.");
            Label(x,337,iw,20,"HIPÓTESIS / EVIDENCIA",small);
            Label(x,363,iw,100,d==null?"Se construye a partir de los movimientos.":string.Join("\n",d.hypotheses??Array.Empty<string>()),small);
            Label(x,471,iw,54,d==null?"":string.Join("\n",(d.evidence??Array.Empty<string>()).Take(2)),small);
            Label(x,534,iw,20,"LIF / Q8 · DIAGNÓSTICO DE OBSERVACIÓN",small);
            Neural(new Rect(x,563,190,190));
            var n=demo?demo.Neural:null;
            Label(x+207,566,142,44,n==null?"Esperando recibo":n.spikes_total+" spikes",heading);
            Label(x+207,620,142,110,"256 unidades\n16 ticks\nDiagnóstico pasivo\ndrives_policy = false",small);
            Label(x,766,iw,35,n==null?(demo?demo.DiagnosticStatus:""):$"Observación #{n.sequence} · {n.observer_seconds*1000:F2} ms del observador",small);
            Label(x,807,iw,44,d==null?"":$"Modelo: {d.learnedActions}/4 comandos\nPresupuesto restante: {d.budgetRemaining:N0}",small);
            Label(x,864,iw,44,$"{world.Moves} movimientos · {world.Scans} giros\n{world.Blocks} bloqueos · perfil {Client.Profile}",small);
            Label(x,924,iw,70,Client.Status,small);
            }
            float y=h-246;
            Label(23,y,290,22,"INSPECCIÓN DEL CUERPO",small);
            if(demo && demo.CloseImage)GUI.DrawTexture(new Rect(23,y+26,310,202),demo.CloseImage,ScaleMode.ScaleToFit,false);
            Label(350,y,340,22,"CÁMARA DEL ARÁCNIDO / RGB-D",small);
            if(world.Sensor.Image)GUI.DrawTexture(new Rect(350,y+34,320,180),world.Sensor.Image,ScaleMode.ScaleToFit,false);
            Label(690,y,180,22,"MAPA OBSERVADO",small);Map(new Rect(690,y+30,185,185),d);
            float bx=897,bw=side-bx-23;
            if(bw>100)
            {
                Label(bx,y,bw,36,demo && demo.Body?$"{demo.Body.PlantedFeet}/8 apoyos\n{demo.Body.MeasuredSpeed:F2} m/s medidos":"Patas procedurales",small);
                GUI.enabled=!Client.Closing;
                if(GUI.Button(new Rect(bx,y+48,bw,31),Client.RunButton,button))Client.ToggleRun();
                GUI.enabled=true;
                if(GUI.Button(new Rect(bx,y+87,bw,31),"Adquirir modelo",button))Client.Train();
                if(GUI.Button(new Rect(bx,y+126,bw,31),"Otro mundo / desde cero",button))
                {var cases=NavigationWorld.Cases().Where(c=>c.split=="development").ToArray();int index=Array.FindIndex(cases,c=>c.id==world.Case.id);Client.Load(cases[(index+1)%cases.Length].id,"cold");}
                if(GUI.Button(new Rect(bx,y+165,bw,31),"Añadir obstáculo",button))Client.PlaceObstacle();
                Label(bx,y+204,bw,36,"ESPACIO pausa · V volúmenes\nRueda zoom · F seguimiento",small);
            }
            GUI.matrix=Matrix4x4.identity;
        }
        void Basal(float x,float y,float w,NavBasalState s)
        {
            Label(x,y,w,25,"ESTADO DEL ADAPTADOR · WSP16",small);
            if(s==null || !s.available || s.E==null || s.E.Length!=6)
            {Label(x,y+40,w,70,"Esperando el primer recibo del puente E[6]/CAM.");return;}
            string[] names={"V / valencia del evento","A / activación sensorial","D = 0 / sin transductor físico asignado","C / soporte del modelo motor","U / urgencia tras bloqueo","B = 0 / sin transductor físico asignado"};
            for(int i=0;i<6;i++)
            {
                float row=y+34+i*37;
                Label(x,row,w-52,19,names[i],small);Label(x+w-49,row,49,20,s.E[i].ToString("+0;-0;0"),small);
                Box(new Rect(x,row+22,w,5),new Color(.13f,.22f,.27f));
                float length=Mathf.Abs(s.E[i])/100f*w/2;
                Box(new Rect(s.E[i]<0?x+w/2-length:x+w/2,row+22,length,5),s.E[i]<0?new Color(.95f,.43f,.25f):cyan);
            }
            Label(x,y+260,w,51,s.cause,small);
            Label(x,y+316,w,26,"GATE / EFECTO EN LA ACCIÓN",small);
            Label(x,y+346,w,28,s.gate);
            if(GUI.Button(new Rect(x,y+378,w,28),"Abrir cadena causal y extracto CAM",button))
                Application.OpenURL(Client.Endpoint+"/?session="+Uri.EscapeDataString(Client.Session));
            Label(x,y+408,w,27,$"CAM  {s.camUsed} / {s.camCapacity}",heading);
            Label(x,y+441,w,36,$"{s.memoryReads} lecturas verificadas · {s.urgencyScans} inspecciones\n{s.memoryVetoes} vetos · {s.camDestroyed} recuerdos destruidos",small);
            if(s.episodes!=null)for(int i=0;i<Math.Min(3,s.episodes.Length);i++)
            {
                var e=s.episodes[i];float ey=y+486+i*64;
                Box(new Rect(x,ey,w,57),new Color(.045f,.077f,.10f));
                Label(x+7,ey+3,w-14,22,$"CAM #{e.slot} · {e.label}",small);
                Label(x+7,ey+26,w-14,28,e.summary,small);
            }
            Label(x,y+687,w,42,s.scope,small);
            Label(x,y+737,w,40,$"Lectura #{s.lastRead} · escritura #{s.lastWritten}\nColor de ojos: U del último recibo",small);
            var d=Client.Latest;
            Label(x,y+787,w,62,$"{Client.Status}\nTrabajo restante: {d.budgetRemaining:N0}",small);
        }
        void Neural(Rect r)
        {
            Box(r,new Color(.023f,.041f,.055f));var n=demo?demo.Neural:null;
            var frame=n?.frames!=null && n.frames.Length>0?n.frames[Mathf.FloorToInt(Time.unscaledTime*8)%n.frames.Length]:null;
            float step=r.width/16;
            for(int i=0;i<256;i++)
            {
                bool spike=frame?.spikes!=null && Array.IndexOf(frame.spikes,i)>=0;
                float potential=frame?.potential!=null && i<frame.potential.Length?Mathf.Clamp01(frame.potential[i]/40f):0;
                Color c=spike?cyan:Color.Lerp(new Color(.09f,.16f,.2f),new Color(.23f,.55f,.60f),potential);
                Box(new Rect(r.x+i%16*step+1,r.y+i/16*step+1,step-2,step-2),c);
            }
        }
        void Map(Rect r,NavDecision d)
        {
            var world=Client.World;float cell=Mathf.Min(r.width/world.Case.width,r.height/world.Case.height);
            Box(r,new Color(.023f,.041f,.055f));
            if(d?.cells!=null)foreach(var p in d.cells)
            {
                Color c=p.state=="occupied"?new Color(.7f,.3f,.22f):p.state=="inferred"?new Color(.68f,.49f,.19f):new Color(.18f,.33f,.40f);
                Box(new Rect(r.x+p.x*cell,r.y+(world.Case.height-1-p.z)*cell,cell-1,cell-1),c);
            }
            var pos=world.Position;Box(new Rect(r.x+(pos.x+.15f)*cell,r.y+(world.Case.height-1-pos.y+.15f)*cell,cell*.7f,cell*.7f),cyan);
            var goal=world.Case.goal;Box(new Rect(r.x+(goal.x+.3f)*cell,r.y+(world.Case.height-1-goal.z+.3f)*cell,cell*.4f,cell*.4f),Color.green);
        }
    }
}
