using UnityEngine;

namespace EchoWorld
{
    [RequireComponent(typeof(Camera))]
    public sealed class IsometricCamera : MonoBehaviour
    {
        public Transform Target;
        public ChunkWorld World;
        public WorldHud Hud;
        public float Yaw = 45;
        Camera lens; Vector3 lastTarget; bool first=true;
        Vector2Int renderedOrigin;
        void Awake()
        {
            lens=GetComponent<Camera>(); lens.orthographic=true; lens.orthographicSize=12;
            lens.nearClipPlane=.1f; lens.farClipPlane=280;
            lens.clearFlags=CameraClearFlags.SolidColor; lens.backgroundColor=new Color(.055f,.105f,.145f);
        }
        void LateUpdate()
        {
            if (!Target) return;
            SynchronizeOrigin();
            bool typing=Hud && Hud.BlocksGameplay;
            if(!typing)
            {
                if(Input.GetKeyDown(KeyCode.Q))Rotate(-1);
                if(Input.GetKeyDown(KeyCode.Z))Rotate(1);
                if(!Hud || !Hud.ConsumesPointer(Input.mousePosition))
                    lens.orthographicSize=Mathf.Clamp(lens.orthographicSize-Input.mouseScrollDelta.y*1.1f,9,25);
            }
            transform.rotation=Quaternion.Euler(35.264f,Yaw,0);
            var target=Target.position+Vector3.up*1;
            bool rebased=(target-lastTarget).sqrMagnitude>4096;
            var wanted=target-transform.forward*70;
            transform.position=(first||rebased)?wanted:Vector3.Lerp(transform.position,wanted,1-Mathf.Exp(-Time.deltaTime*10));
            lastTarget=target; first=false;
            Shader.SetGlobalVector("_EchoPlayer",Target.position+Vector3.up);
            Shader.SetGlobalVector("_EchoCamera",transform.position);
            Shader.SetGlobalFloat("_EchoCutawayRadius",2.4f);
        }
        // Picking runs in Update, before the normal camera LateUpdate. Keep its
        // ray in the same local coordinate frame as newly rebased colliders.
        public void SynchronizeOrigin()
        {
            if(!World)return;
            Vector2Int change=World.Origin-renderedOrigin;
            if(change==Vector2Int.zero)return;
            Vector3 shift=new Vector3(change.x,0,change.y);
            transform.position-=shift;lastTarget-=shift;renderedOrigin=World.Origin;
        }
        public void Rotate(int quarterTurns)
        {Yaw+=quarterTurns*90;SnapToTarget();}
        public void SnapToTarget()
        {
            if(!Target)return;
            transform.rotation=Quaternion.Euler(35.264f,Yaw,0);
            lastTarget=Target.position+Vector3.up;
            transform.position=lastTarget-transform.forward*70;
            if(World)renderedOrigin=World.Origin;
            first=false;
        }
    }
}
