using UnityEngine;
namespace EchoStation
{
    public sealed class StationObject : MonoBehaviour
    {
        public string Id, Kind, Label;
        public int X,Z;
        public bool Inspected, Compatible, Active, Locked, Collected;
        public GameObject Visual;
        public ObjectData PublicData() => new ObjectData {id=Id,kind=Kind,label=Label,x=X,z=Z,
            inspected=Inspected,compatible=Inspected && Compatible,active=Active,locked=Locked};
    }
}
