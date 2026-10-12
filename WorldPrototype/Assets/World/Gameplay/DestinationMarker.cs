using UnityEngine;

namespace EchoWorld
{
    public sealed class DestinationMarker : MonoBehaviour
    {
        public ChunkWorld World;
        LineRenderer ring;Material material;Vector3Int cell;float until;
        void Awake()
        {
            ring=gameObject.AddComponent<LineRenderer>();material=new Material(Shader.Find("Sprites/Default"));
            ring.sharedMaterial=material;ring.useWorldSpace=true;ring.loop=true;ring.widthMultiplier=.055f;ring.positionCount=0;
        }
        public void Show(Vector3Int destination,bool valid)
        {cell=destination;until=Time.time+1.6f;ring.startColor=ring.endColor=valid?new Color(.85f,.84f,.53f):new Color(.8f,.18f,.1f);}
        void LateUpdate()
        {
            if(!World||Time.time>=until){ring.positionCount=0;return;}
            float t=1-(until-Time.time)/1.6f;float radius=.32f+Mathf.Sin(t*Mathf.PI)*.22f;
            ring.positionCount=40;Vector3 p=World.ToLocal(cell)+new Vector3(.5f,.045f,.5f);
            for(int i=0;i<40;i++){float a=i*Mathf.PI*2/40;ring.SetPosition(i,p+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
        }
        void OnDestroy(){if(material)Destroy(material);}
    }
}
