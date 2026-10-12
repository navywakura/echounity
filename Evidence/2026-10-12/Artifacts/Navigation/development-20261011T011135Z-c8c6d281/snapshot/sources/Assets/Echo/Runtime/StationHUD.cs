using UnityEngine;

namespace EchoStation
{
    public sealed class StationHUD : MonoBehaviour
    {
        public StationClient Client;
        GUIStyle title,large,label,small,button,tag;
        Texture2D white;
        float extraWidth;
        Rect R(float x,float y,float width,float height) => new Rect(x>=887?x+extraWidth:x,y,width,height);
        Color cyan=new Color(.32f,.94f,.83f),muted=new Color(.54f,.65f,.7f);
        void Styles()
        {
            if(white)return;white=Texture2D.whiteTexture;
            title=new GUIStyle(GUI.skin.label){fontSize=27,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            large=new GUIStyle(title){fontSize=21};
            label=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true};label.normal.textColor=new Color(.85f,.9f,.94f);
            small=new GUIStyle(label){fontSize=14};small.normal.textColor=muted;
            tag=new GUIStyle(small){fontSize=12,fontStyle=FontStyle.Bold};tag.normal.textColor=cyan;
            button=new GUIStyle(GUI.skin.button){fontSize=16,padding=new RectOffset(12,12,8,8)};
        }
        void Box(Rect r,Color color){var prior=GUI.color;GUI.color=color;GUI.DrawTexture(r,white);GUI.color=prior;}
        void Text(float x,float y,float w,float h,string s,GUIStyle style){GUI.Label(R(x,y,w,h),s??"—",style);}
        void OnGUI()
        {
            Styles();float scale=Mathf.Min(Screen.width/1600f,Screen.height/1000f);
            extraWidth=Screen.width/scale-1600;
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            if(Camera.main)Camera.main.rect=new Rect(0,.063f,(Screen.width-422*scale)/Screen.width,.861f);
            var w=Client.World;var d=Client.Latest;
            if(string.IsNullOrEmpty(d?.schema))d=null;
            Box(R(0,0,1600+extraWidth,76),new Color(.025f,.044f,.062f,.98f));
            Box(R(1178,76,422,924),new Color(.025f,.044f,.062f,.98f));
            Box(R(0,937,1178+extraWidth,63),new Color(.025f,.044f,.062f,.95f));
            Text(28,18,240,42,"ECHO / STATION",title);
            Text(297,23,400,28,"01     LABORATORIO SITUADO",tag);
            Text(887,24,270,30,Client.Connected?"●  NÚCLEO LOCAL CONECTADO":"○  PUENTE DESCONECTADO",tag);
            Text(1205,22,360,30,"TELEMETRÍA DE DECISIONES",tag);
            Text(26,95,430,32,w.Mission=="delivery"?"02 / Logística de la estación":w.Mission=="detour"?"03 / Consecuencias y desvíos":"01 / Restablecer el riego",large);
            Text(26,130,510,46,"Estación isométrica · observación parcial · sin LLM",small);
            float y=102;
            Text(1205,y,360,25,"MISIÓN ACTIVA",tag);y+=28;
            Text(1205,y,363,65,w.Mission=="delivery"?"Recoge el paquete, confirma la entrega y vuelve a carga.":"Alimenta la bomba, abre el riego y vuelve a carga.",large);y+=83;
            Box(R(1205,y,364,5),new Color(.12f,.19f,.24f));Box(R(1205,y,3.64f*w.Energy,5),cyan);y+=13;
            Text(1205,y,360,24,"ENERGÍA  "+Mathf.CeilToInt(w.Energy)+" %          ACCIONES  "+w.Actions,small);y+=36;
            if(GUI.Button(R(1205,y,175,42),Client.Automatic?"Pausar ECHO":"Iniciar ECHO",button)){if(Client.Automatic)Client.Pause();else Client.Begin();}
            if(GUI.Button(R(1393,y,176,42),"Nueva misión",button))Client.ResetMission(w.Mission);
            y+=53;
            if(GUI.Button(R(1205,y,115,30),"Riego",button))Client.ResetMission("irrigation");
            if(GUI.Button(R(1330,y,115,30),"Entrega",button))Client.ResetMission("delivery");
            if(GUI.Button(R(1455,y,114,30),"Desvío",button))Client.ResetMission("detour");
            y+=46;
            Text(1205,y,360,56,Client.Status,label);y+=68;
            Text(1205,y,360,23,"OBSERVACIÓN / INVENTARIO",tag);y+=26;
            Text(1205,y,360,47,"Celda ("+w.Position.x+", "+w.Position.y+") · "+(w.Inventory.Count==0?"inventario vacío":string.Join(", ",w.Inventory)),label);y+=52;
            Text(1205,y,360,23,"HIPÓTESIS Y EVIDENCIA",tag);y+=26;
            string hypotheses=d?.hypotheses!=null?string.Join("\n",d.hypotheses):"Esperando la primera decisión registrada.";
            if(d?.evidence!=null && d.evidence.Length>0) hypotheses+="\nEvidencia: "+d.evidence[0];
            Text(1205,y,364,99,hypotheses,small);y+=103;
            Text(1205,y,360,23,"PREDICCIÓN → CONSECUENCIA",tag);y+=26;
            Text(1205,y,365,48,d?.prediction??"Sin predicción pendiente.",label);y+=53;
            Text(1205,y,365,67,w.LastResult,small);y+=75;
            Text(1205,y,360,23,"PRESUPUESTO / PROCEDENCIA",tag);y+=26;
            Text(1205,y,365,48,d==null?"Planificador externo listo; trabajo aún no iniciado.":d.budgetUsed.ToString("N0")+" unidades usadas · "+d.budgetRemaining.ToString("N0")+" restantes",small);y+=50;
            Text(1205,y,366,48,"ECHO-5 · planificador acotado\nRegistro: thought.jsonl",small);
            Text(25,955,1150,30,"ESPACIO  iniciar / pausar     WASD / CLIC  mover     E  usar     I  inspeccionar     V  predicción     B  obstáculo     F  seguir     RUEDA  zoom",small);
            if(w.Completed)
            {
                Box(new Rect(340,815,500,83),new Color(.025f,.15f,.15f,.97f));
                Text(367,830,452,29,"MISIÓN VERIFICADA",large);
                Text(367,863,452,25,"Objetivo observado y regreso a la zona de carga.",small);
            }
        }
    }
}
