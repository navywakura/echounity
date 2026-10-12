using UnityEngine;

namespace EchoStation.Arachnid
{
    // Presentation only; E[6] arrives with a real station decision.
    public sealed class StationArachnid : MonoBehaviour
    {
        StationClient client;ArachnidGait body;
        void Start(){client=GetComponent<StationClient>();body=client.World.Robot.GetComponentInChildren<ArachnidGait>();}
        void LateUpdate()
        {
            var e=client?.Latest?.basal?.E;
            if(body && e!=null && e.Length==6)
                body.SensorColor=Color.Lerp(new Color(0,.898f,1),new Color(1,.25f,.07f),Mathf.Clamp01((e[4]-10)/70f));
        }
        void OnGUI()
        {
            if(!client)return;
            var s=client.Latest?.basal;
            var style=new GUIStyle(GUI.skin.box){fontSize=14,alignment=TextAnchor.UpperLeft,wordWrap=true};
            string info=s==null?"E[6] y CAM: esperando el primer recibo.":
                $"E[6]  V {s.E[0]}  A {s.E[1]}  C {s.E[3]}  U {s.E[4]}\nCAM {s.camUsed}/4096 · {s.gate}\n{s.cause}";
            GUI.Box(new Rect(18,Screen.height-158,450,98),info+"\nD = 0 · B = 0: sin transductor físico asignado",style);
            if(GUI.Button(new Rect(18,Screen.height-58,450,30),"Abrir inspector causal y recibos"))
                Application.OpenURL(client.Endpoint+"/?session="+System.Uri.EscapeDataString(client.Session));
        }
    }
}
