using UnityEngine;

namespace EchoStation
{
    public sealed class IsoCamera : MonoBehaviour
    {
        public Transform Follow;
        public bool Tracking;
        public Vector3 Focus=new Vector3(14,0,12);
        Camera view;
        void Awake() {view=GetComponent<Camera>();}
        void LateUpdate()
        {
            if(view==null)view=GetComponent<Camera>();
            if(Input.GetKeyDown(KeyCode.F))Tracking=!Tracking;
            if(Input.GetKeyDown(KeyCode.Home)){Tracking=false;Focus=new Vector3(14,0,12);view.orthographicSize=17.8f;}
            if(!Tracking && Input.GetMouseButton(2)) Focus+=(-transform.right*Input.GetAxis("Mouse X")-Vector3.ProjectOnPlane(transform.up,Vector3.up)*Input.GetAxis("Mouse Y"))*.35f;
            if(Tracking && Follow)Focus=Vector3.Lerp(Focus,Follow.position,1-Mathf.Exp(-5*Time.unscaledDeltaTime));
            view.orthographicSize=Mathf.Clamp(view.orthographicSize-Input.mouseScrollDelta.y,5,25);
            transform.rotation=Quaternion.Euler(35.26439f,-45,0);
            transform.position=Focus-transform.forward*45;
        }
    }
}
