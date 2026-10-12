using UnityEngine;

namespace EchoWorld
{
    public sealed class HumanoidVisual : MonoBehaviour
    {
        Transform torso, leftLeg, rightLeg, leftArm, rightArm;
        Material[] ownedMaterials;
        float cycle; Vector3 velocity; bool grounded=true, running; float vertical;
        public void SetMotion(Vector3 value,bool onGround,bool sprint,float fall)
        {velocity=value;grounded=onGround;running=sprint;vertical=fall;}
        public void SetVelocity(Vector3 value) { velocity = value; }
        public static Material MakeMaterial(string name, Color color, bool glow = false)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, color = color };
            m.SetFloat("_Glossiness", .22f);
            if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * .8f); }
            return m;
        }
        public static Transform Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.layer = 2;
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            return go.transform;
        }
        Transform Pivot(string name, Vector3 position)
        {
            var t = new GameObject(name).transform; t.SetParent(transform, false); t.localPosition = position; return t;
        }
        void Awake()
        {
            Material suit=MakeMaterial("Ivory suit",new Color(.84f,.87f,.86f));
            Material dark=MakeMaterial("Graphite joints",new Color(.10f,.16f,.20f));
            Material orange=MakeMaterial("Expedition amber",new Color(1,.51f,.19f));
            Material visor=MakeMaterial("Cyan visor",new Color(.08f,.85f,.94f),true);
            ownedMaterials=new[]{suit,dark,orange,visor};
            torso=Part(transform,"Torso",new Vector3(0,1.15f,0),new Vector3(.48f,.56f,.32f),suit);
            Part(transform,"Harness",new Vector3(0,1.30f,.171f),new Vector3(.39f,.10f,.045f),orange);
            Part(transform,"Oxygen pack",new Vector3(0,1.19f,-.24f),new Vector3(.32f,.43f,.17f),dark);
            Part(transform,"Helmet",new Vector3(0,1.67f,0),new Vector3(.46f,.41f,.40f),suit);
            Part(transform,"Visor",new Vector3(0,1.69f,.212f),new Vector3(.37f,.19f,.035f),visor);
            Part(transform,"Belt",new Vector3(0,.85f,0),new Vector3(.42f,.14f,.30f),dark);
            leftLeg=Pivot("Left leg",new Vector3(-.13f,.80f,0)); rightLeg=Pivot("Right leg",new Vector3(.13f,.80f,0));
            foreach (Transform leg in new[]{leftLeg,rightLeg})
            { Part(leg,"Leg",new Vector3(0,-.29f,0),new Vector3(.18f,.55f,.20f),suit);
              Part(leg,"Boot",new Vector3(0,-.67f,.04f),new Vector3(.22f,.23f,.33f),dark); }
            leftArm=Pivot("Left arm",new Vector3(-.32f,1.39f,0)); rightArm=Pivot("Right arm",new Vector3(.32f,1.39f,0));
            foreach (Transform arm in new[]{leftArm,rightArm})
            { Part(arm,"Sleeve",new Vector3(0,-.21f,0),new Vector3(.18f,.43f,.20f),suit);
              Part(arm,"Glove",new Vector3(0,-.47f,0),new Vector3(.19f,.15f,.21f),orange); }
        }
        void Update()
        {
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            cycle += Mathf.Min(speed, 6)*Time.deltaTime*2.4f;
            float swing = Mathf.Sin(cycle)*Mathf.Clamp01(speed)*(running?44:27);
            if(!grounded)
            {
                leftLeg.localRotation=Quaternion.Euler(vertical>0?-35:18,0,0);
                rightLeg.localRotation=Quaternion.Euler(vertical>0?25:-12,0,0);
                leftArm.localRotation=Quaternion.Euler(-30,0,-24);rightArm.localRotation=Quaternion.Euler(-30,0,24);
                if(speed>.1f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(new Vector3(velocity.x,0,velocity.z)),Time.deltaTime*12);
                return;
            }
            leftLeg.localRotation=Quaternion.Euler(swing,0,0); rightLeg.localRotation=Quaternion.Euler(-swing,0,0);
            leftArm.localRotation=Quaternion.Euler(-swing*.7f,0,-5); rightArm.localRotation=Quaternion.Euler(swing*.7f,0,5);
            if(speed>.1f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(new Vector3(velocity.x,0,velocity.z)),Time.deltaTime*12);
        }
        void OnDestroy()
        {
            if(ownedMaterials==null)return;
            foreach(var material in ownedMaterials)
            {if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
        }
    }
}
