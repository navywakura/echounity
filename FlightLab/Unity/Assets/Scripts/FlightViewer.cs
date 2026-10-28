using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlightViewer : MonoBehaviour {
    readonly Color ink=new Color(.035f,.06f,.075f), teal=new Color(.2f,.88f,.78f), orange=new Color(1f,.57f,.22f);
    Camera cameraRig; GameObject drone, geometry, knowledge; Transform[] rotors;
    LineRenderer route, beams, targetLine; Material lineMat;
    List<FlightFrame> frames=new List<FlightFrame>(); List<Vector3> trail=new List<Vector3>();
    Dictionary<string,GameObject> cells=new Dictionary<string,GameObject>();
    Dictionary<string,Material> materials=new Dictionary<string,Material>();
    FlightFrame current; string directory,error="", capture; bool live,paused,knownView; int mode,index;
    float playhead,speed=1,readAt,drawAt,yaw=35,pitch=55,distance=29,quitAt; bool captured;
    GUIStyle title,label,small,button,big; Texture2D pixel; float uiScale=1;
    string recordDirectory,caption; bool recording,tour; int recordFps=24,recordFrame,recordTotal;
    float recordStart; StreamWriter recordLog;

    string Arg(string name,string fallback="") {
        string[] args=Environment.GetCommandLineArgs();
        for(int i=0;i+1<args.Length;i++) if(args[i]==name) return args[i+1];
        return fallback;
    }
    bool Has(string arg) { return Array.IndexOf(Environment.GetCommandLineArgs(),arg)>=0; }
    void Start() {
        Application.targetFrameRate=30; QualitySettings.vSyncCount=0; QualitySettings.shadows=ShadowQuality.Disable;
        directory=Arg("--flight-dir",Path.Combine(Application.streamingAssetsPath,"Demo"));
        live=Has("--live"); knownView=Has("--observed"); capture=Arg("--capture"); float.TryParse(Arg("--quit-after","0"),out quitAt);
        float requestedSpeed; if(float.TryParse(Arg("--speed","1"),out requestedSpeed) && (requestedSpeed==1 || requestedSpeed==2 || requestedSpeed==4)) speed=requestedSpeed;
        caption=Arg("--caption",""); recordDirectory=Arg("--record-dir",""); tour=Has("--tour");
        string selectedCamera=Arg("--camera","overview");
        mode=selectedCamera=="follow"?1:selectedCamera=="onboard"?2:0;
        lineMat=new Material(Resources.Load<Material>("FlightLine"));
        RenderSettings.ambientLight=new Color(.65f,.7f,.74f); RenderSettings.fog=false;
        var light=new GameObject("Sun").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.1f;
        light.transform.rotation=Quaternion.Euler(50,-35,0);
        cameraRig=new GameObject("Camera").AddComponent<Camera>(); cameraRig.clearFlags=CameraClearFlags.SolidColor;
        cameraRig.backgroundColor=new Color(.055f,.09f,.105f); cameraRig.fieldOfView=48;
        cameraRig.rect=new Rect(.01f,.12f,.75f,.76f);
        geometry=new GameObject("Observer-only world"); knowledge=new GameObject("Observed cells");
        BuildDrone(); route=Line("Recorded trajectory",teal,.045f); beams=Line("LiDAR",new Color(.1f,.6f,.55f,.45f),.015f);
        targetLine=Line("Goal",orange,.035f);
        try {
            var scene=JsonUtility.FromJson<FlightScene>(File.ReadAllText(Path.Combine(directory,"scenario/scene.json")));
            foreach(var b in scene.boxes) {
                var obj=Box(b.name,FlightCoordinates.Position(b.position),FlightCoordinates.Position(b.size),new Color(b.color[0],b.color[1],b.color[2]));
                obj.transform.parent=geometry.transform;
                // Physically solid boxes are styled visually; no colliders affect the drone.
                if(b.kind=="rack") AddRackStripes(obj,b);
            }
            for(int x=-9;x<=9;x++) GridLine(new Vector3(x,.015f,-4),new Vector3(x,.015f,12));
            for(int z=-4;z<=12;z++) GridLine(new Vector3(-9,.015f,z),new Vector3(9,.015f,z));
            Box("Landing pad",new Vector3(0,.025f,0),new Vector3(1.5f,.025f,1.5f),teal*.6f);
            Box("H1",new Vector3(-.25f,.05f,0),new Vector3(.07f,.015f,.65f),Color.white);
            Box("H2",new Vector3(.25f,.05f,0),new Vector3(.07f,.015f,.65f),Color.white);
            Box("H3",new Vector3(0,.05f,0),new Vector3(.5f,.015f,.07f),Color.white);
            if(!live) {
                foreach(var text in File.ReadLines(Path.Combine(directory,"frames.jsonl"))) {
                    var f=JsonUtility.FromJson<FlightFrame>(text);
                    if(!FlightCoordinates.Valid(f)) throw new InvalidDataException("Invalid recorded frame");
                    frames.Add(f);
                }
                if(frames.Count==0) throw new InvalidDataException("No recorded telemetry");
                float offset=0; float.TryParse(Arg("--start-at","0"),out offset);
                playhead=Mathf.Clamp(frames[0].time+offset,frames[0].time,frames[frames.Count-1].time); current=frames[0];
            }
        } catch(Exception ex) { error=ex.Message; Debug.LogError("Flight data: "+error); }
        geometry.SetActive(!knownView);
        Debug.Log("FlightViewer ready: "+(live?"LIVE ":"REPLAY ")+directory+" frames="+frames.Count);
        if(recordDirectory!="") {
            try {
                if(live || frames.Count==0 || error!="") throw new InvalidOperationException("Video export requires a valid recorded flight");
                if(Directory.Exists(recordDirectory)) throw new IOException("Preserve the previous recording; choose a new directory");
                int.TryParse(Arg("--record-fps","24"),out recordFps); recordFps=Mathf.Clamp(recordFps,12,30);
                float duration=(frames[frames.Count-1].time-playhead)/speed+2;
                float limit; if(float.TryParse(Arg("--record-seconds","0"),out limit) && limit>0) duration=Mathf.Min(duration,limit);
                recordTotal=Mathf.CeilToInt(duration*recordFps); recordStart=playhead;
                if(recordTotal<1 || recordTotal>18000) throw new InvalidOperationException("Bounded video duration exceeded");
                Directory.CreateDirectory(recordDirectory); recordLog=new StreamWriter(Path.Combine(recordDirectory,"frames.jsonl"));
                recording=true; Time.captureFramerate=recordFps; Application.targetFrameRate=recordFps;
                WriteRecordReceipt("RECORDING"); StartCoroutine(RecordVideo());
            } catch(Exception ex) { Debug.LogError("Recording: "+ex.Message); Application.Quit(3); }
        }
    }

    Material Mat(Color c) {
        string key=ColorUtility.ToHtmlStringRGBA(c);
        if(!materials.ContainsKey(key)) { var m=new Material(Resources.Load<Material>("FlightSurface")); m.color=c; m.SetFloat("_Glossiness",.2f); materials[key]=m; }
        return materials[key];
    }
    GameObject Box(string name,Vector3 p,Vector3 size,Color c,Transform parent=null) {
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube); o.name=name; Destroy(o.GetComponent<Collider>());
        o.transform.SetParent(parent,false); o.transform.localPosition=p; o.transform.localScale=size; o.GetComponent<Renderer>().sharedMaterial=Mat(c); return o;
    }
    void AddRackStripes(GameObject obj,SceneBox b) {
        for(int i=0;i<3;i++) {
            var rail=Box("Rack shelf",new Vector3(0,-.35f+i*.3f,0),new Vector3(1.03f,.025f,1.02f),new Color(.9f,.55f,.19f),obj.transform);
        }
    }
    LineRenderer Line(string name,Color c,float width) {
        var l=new GameObject(name).AddComponent<LineRenderer>(); l.sharedMaterial=lineMat;
        l.startColor=l.endColor=c; l.startWidth=l.endWidth=width; l.positionCount=0; return l;
    }
    void GridLine(Vector3 a,Vector3 b) {
        var l=Line("Floor grid",new Color(.35f,.43f,.45f),.012f); l.positionCount=2; l.SetPositions(new[]{a,b});
        l.transform.parent=geometry.transform;
    }
    void BuildDrone() {
        drone=new GameObject("X500 simplified visual - Gazebo pose only");
        // x500 includes x500_base at z=.24. Telemetry is the model origin.
        var body=new GameObject("X500 base frame"); body.transform.SetParent(drone.transform,false); body.transform.localPosition=Vector3.up*.24f;
        Box("Body",new Vector3(0,.025f,0),new Vector3(.22f,.10f,.32f),new Color(.08f,.11f,.13f),body.transform);
        Box("Battery",new Vector3(0,.1f,0),new Vector3(.13f,.08f,.24f),orange,body.transform);
        rotors=new Transform[4]; int i=0;
        foreach(int x in new[]{-1,1}) foreach(int z in new[]{-1,1}) {
            var arm=Box("Carbon arm",new Vector3(x*.087f,.03f,z*.087f),new Vector3(.045f,.04f,.25f),new Color(.22f,.26f,.27f),body.transform);
            arm.transform.localRotation=Quaternion.Euler(0,x*z*45,0);
            var rotor=new GameObject("Rotor visual"); rotor.transform.SetParent(body.transform); rotor.transform.localPosition=new Vector3(x*.174f,.08f,z*.174f);
            Box("Propeller",Vector3.zero,new Vector3(.285f,.012f,.025f),z>0?teal:new Color(.65f,.68f,.7f),rotor.transform); rotors[i++]=rotor.transform;
            Box("Motor",new Vector3(x*.174f,.045f,z*.174f),new Vector3(.055f,.04f,.055f),Color.black,body.transform);
        }
        foreach(int x in new[]{-1,1}) {
            Box("Landing leg",new Vector3(x*.12f,-.1f,0),new Vector3(.02f,.2f,.02f),Color.gray,body.transform);
            Box("Skid",new Vector3(x*.12f,-.2f,0),new Vector3(.02f,.02f,.3f),Color.gray,body.transform);
        }
        Box("LiDAR",new Vector3(0,.28f,0),new Vector3(.065f,.04f,.065f),teal,body.transform);
    }
    void Update() {
        if(!recording) {
            if(Input.GetKeyDown(KeyCode.Space)) paused=!paused;
            if(Input.GetKeyDown(KeyCode.K)) { knownView=!knownView; geometry.SetActive(!knownView); }
            if(Input.GetKeyDown(KeyCode.Alpha1)) mode=0;
            if(Input.GetKeyDown(KeyCode.Alpha2)) mode=1;
            if(Input.GetKeyDown(KeyCode.Alpha3)) mode=2;
        }
        if(Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
        if(live && !paused && Time.unscaledTime>readAt) {
            readAt=Time.unscaledTime+.2f;
            try { var f=JsonUtility.FromJson<FlightFrame>(File.ReadAllText(Path.Combine(directory,"live.json"))); if(FlightCoordinates.Valid(f)) { current=f; error=""; } }
            catch(Exception ex) { error="Esperando telemetría: "+ex.GetType().Name; }
        } else if(!live && frames.Count>0) {
            if(recording) {
                playhead=Mathf.Min(recordStart+recordFrame*speed/recordFps,frames[frames.Count-1].time);
                if(tour) {
                    float part=(float)recordFrame/recordTotal;
                    mode=part>.3f && part<.65f?1:0; knownView=part>=.65f && part<.9f;
                    geometry.SetActive(!knownView);
                }
            } else if(!paused) playhead=Mathf.Min(playhead+Time.deltaTime*speed,frames[frames.Count-1].time);
            while(index+1<frames.Count && frames[index+1].time<=playhead) index++;
            while(index>0 && frames[index].time>playhead) index--;
            current=frames[index];
        }
        if(current!=null) {
            Vector3 pos=FlightCoordinates.Position(current.position);
            drone.transform.position=pos; drone.transform.rotation=FlightCoordinates.Attitude(current.rotation);
            foreach(var r in rotors) if(!paused && !FlightCoordinates.Terminal(current.phase)) r.Rotate(0,1200*Time.deltaTime,0);
            if(trail.Count==0 || Vector3.Distance(trail[trail.Count-1],pos)>.07f) {
                trail.Add(pos); if(trail.Count>4000) trail.RemoveAt(0);
                route.positionCount=trail.Count; route.SetPositions(trail.ToArray());
            }
            if(recording || Time.unscaledTime>drawAt) { drawAt=Time.unscaledTime+.3f; DrawKnowledge(); }
        }
        if(!recording) {
            if(Input.GetMouseButton(1)) { yaw+=Input.GetAxis("Mouse X")*3; pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*3,15,85); }
            distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*1.5f,3,40);
        }
        Vector3 focus=mode==0?new Vector3(0,0,4):(current!=null?drone.transform.position:Vector3.zero);
        float d=mode==0?distance:mode==1?6:2;
        if(mode==2 && current!=null) { cameraRig.transform.position=drone.transform.TransformPoint(Vector3.up*.54f); cameraRig.transform.rotation=drone.transform.rotation; }
        else { cameraRig.transform.position=focus+Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-d); cameraRig.transform.LookAt(focus); }
        if(!captured && capture!="" && Time.unscaledTime>4) { captured=true; StartCoroutine(Capture()); }
        if(!recording && quitAt>0 && Time.unscaledTime>=quitAt) Application.Quit();
    }
    [Serializable] class RecordedFrame { public int frame,sourceFrame,camera; public float simulatedTime; public bool observed; public string phase; }
    [Serializable] class RecordReceipt { public string status,source,caption; public int fps,frames,expectedFrames,width,height; public float speed; }
    void WriteRecordReceipt(string status) {
        var r=new RecordReceipt { status=status,source=Path.GetFileName(directory),caption=caption,fps=recordFps,frames=recordFrame,expectedFrames=recordTotal,width=Screen.width,height=Screen.height,speed=speed };
        File.WriteAllText(Path.Combine(recordDirectory,"recording.json"),JsonUtility.ToJson(r,true));
    }
    IEnumerator RecordVideo() {
        var buffer=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
        while(recordFrame<recordTotal) {
            yield return new WaitForEndOfFrame();
            buffer.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0,false); buffer.Apply(false,false);
            File.WriteAllBytes(Path.Combine(recordDirectory,"frame-"+recordFrame.ToString("D6")+".png"),buffer.EncodeToPNG());
            recordLog.WriteLine(JsonUtility.ToJson(new RecordedFrame { frame=recordFrame,sourceFrame=index,camera=mode,simulatedTime=current.time,observed=knownView,phase=current.phase }));
            recordFrame++; if(recordFrame%recordFps==0) recordLog.Flush();
        }
        recordLog.Dispose(); recordLog=null; Destroy(buffer); WriteRecordReceipt("COMPLETE");
        Debug.Log("Recording complete: "+recordFrame+" frames, "+speed+"x recorded flight"); Application.Quit();
    }
    void OnDestroy() { if(recordLog!=null) recordLog.Dispose(); }
    IEnumerator Capture() { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(capture); Debug.Log("Screenshot requested "+capture); }
    void DrawKnowledge() {
        foreach(var o in cells.Values) o.SetActive(false);
        if(current.known!=null) foreach(var c in current.known) {
            string key=c.x+","+c.y;
            if(!cells.ContainsKey(key)) cells[key]=Box("Observed cell",new Vector3(c.x,.05f,c.y),new Vector3(.46f,.035f,.46f),teal,knowledge.transform);
            var obj=cells[key]; obj.SetActive(true); obj.GetComponent<Renderer>().sharedMaterial=Mat(c.occupied?orange:teal*.42f);
            obj.transform.localScale=new Vector3(.46f,c.occupied?.25f:.035f,.46f);
        }
        var v=new List<Vector3>(); Vector3 origin=drone.transform.TransformPoint(Vector3.up*.52f);
        if(current.rays!=null) foreach(var r in current.rays) { v.Add(origin); v.Add(new Vector3(r.x,origin.y,r.y)); v.Add(origin); }
        beams.positionCount=v.Count; beams.SetPositions(v.ToArray());
        if(current.target!=null) { targetLine.positionCount=2; targetLine.SetPositions(new[]{drone.transform.position,FlightCoordinates.Position(current.target)}); }
    }
    void Styles() {
        if(title!=null) return;
        pixel=new Texture2D(1,1); pixel.SetPixel(0,0,Color.white); pixel.Apply();
        title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold}; title.normal.textColor=Color.white;
        label=new GUIStyle(GUI.skin.label){fontSize=15}; label.normal.textColor=new Color(.85f,.91f,.92f);
        small=new GUIStyle(label){fontSize=12,wordWrap=true}; small.normal.textColor=new Color(.55f,.68f,.7f);
        big=new GUIStyle(title){fontSize=30}; big.normal.textColor=teal;
        button=new GUIStyle(GUI.skin.button){fontSize=13,padding=new RectOffset(12,12,8,8)};
    }
    void Panel(Rect r,Color c) { Color old=GUI.color; GUI.color=c; GUI.DrawTexture(r,pixel); GUI.color=old; }
    void OnGUI() {
        Styles(); uiScale=Mathf.Min(Screen.width/1280f,Screen.height/800f); GUI.matrix=Matrix4x4.Scale(Vector3.one*uiScale);
        float w=Screen.width/uiScale,h=Screen.height/uiScale;
        Panel(new Rect(0,0,w,88),ink);
        GUI.Label(new Rect(26,17,440,34),"ECHO / FLIGHT LAB",title);
        GUI.Label(new Rect(27,53,850,25),caption==""?"EXPLORAR  ·  RECORDAR  ·  REGRESAR":caption,small);
        GUI.Label(new Rect(w-340,19,320,24),live?"●  TELEMETRÍA LOCAL":"REPLAY "+speed+"×  /  VUELO REGISTRADO",label);
        GUI.Label(new Rect(w-340,47,320,24),"PX4 + Gazebo  /  X500",small);
        Panel(new Rect(w-298,108,276,h-224),new Color(.035f,.065f,.08f,.96f));
        GUI.Label(new Rect(30,105,w-360,24),knownView?"MAPA OBSERVADO POR EL AGENTE":"ESCENARIO COMPLETO · VISTA DEL OPERADOR",small);
        float x=w-277; GUI.Label(new Rect(x,125,250,25),"ESTADO DE MISIÓN",small);
        if(current!=null) {
            big.normal.textColor=FlightCoordinates.Terminal(current.phase) && current.phase!="MISSION_COMPLETE"?orange:teal;
            GUI.Label(new Rect(x,155,250,40),Phase(current.phase),big);
            string[] rows={"Política                  "+current.arm,"Tiempo simulado      "+current.time.ToString("F1")+" s",
                "Recorrido                "+current.travel_m.ToString("F1")+" m","Pausas iniciadas        "+current.inspections,
                "Omitidas / memoria   "+current.skipped,"Zonas visitadas         "+current.visited,
                "Celdas observadas     "+(current.known==null?0:current.known.Length),"Contactos obstáculo  "+(current.contacts<0?"sin verificar":current.contacts.ToString())};
            for(int i=0;i<rows.Length;i++) GUI.Label(new Rect(x,211+i*31,250,27),rows[i],label);
            GUI.Label(new Rect(x,475,245,24),"DECISIÓN OBSERVADA",small);
            GUI.Label(new Rect(x,505,235,66),Translate(current.reason),new GUIStyle(label){wordWrap=true});
            GUI.Label(new Rect(x,580,237,60),"Presupuesto: tiempo simulado.\nEl consumo eléctrico aún no está calibrado.",small);
            if(live && !paused && (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0-current.wall_unix)>3)
                GUI.Label(new Rect(30,110,650,35),"TELEMETRÍA DETENIDA — última pose recibida",title);
        }
        if(error!="") GUI.Label(new Rect(30,150,w-360,65),error,label);
        Panel(new Rect(0,h-90,w,90),ink);
        GUI.enabled=!recording;
        if(GUI.Button(new Rect(24,h-73,118,34),paused?"Reproducir":"Pausar vista",button)) paused=!paused;
        if(GUI.Button(new Rect(153,h-73,110,34),"General [1]",button)) mode=0;
        if(GUI.Button(new Rect(273,h-73,130,34),"Seguir dron [2]",button)) mode=1;
        if(GUI.Button(new Rect(413,h-73,130,34),"A bordo [3]",button)) mode=2;
        if(GUI.Button(new Rect(553,h-73,160,34),knownView?"Ver escenario [K]":"Lo observado [K]",button)) { knownView=!knownView; geometry.SetActive(!knownView); }
        if(GUI.Button(new Rect(723,h-73,85,34),speed+"×",button)) speed=speed==1?2:speed==2?4:1;
        if(!live && frames.Count>0) {
            float next=GUI.HorizontalSlider(new Rect(835,h-61,w-865,25),playhead,frames[0].time,frames[frames.Count-1].time);
            if(Mathf.Abs(next-playhead)>.1f) { playhead=next; trail.Clear(); }
        }
        GUI.Label(new Rect(25,h-30,w-40,22),"Botón derecho: orbitar  ·  Rueda: zoom  ·  Espacio: pausa  ·  ESC: salir    |    Física en Gazebo; Unity muestra la grabación",small);
        GUI.enabled=true;
    }
    string Phase(string value) {
        switch(value) {
            case "TAKEOFF": return "DESPEGANDO";
            case "EXPLORE": return "EXPLORANDO";
            case "RETURN": return "REGRESANDO";
            case "LANDING": return "ATERRIZANDO";
            case "MISSION_COMPLETE": return "COMPLETADA";
            case "RETURNED_PARTIAL": return "VUELTA PARCIAL";
            case "CONTINGENCY_LANDED": return "CONTINGENCIA";
            case "LANDING_INCOMPLETE": return "SIN CONFIRMAR";
            case "TAKEOFF_UNCONFIRMED": return "SIN DESPEGUE";
            default: return value;
        }
    }
    string Translate(string value) {
        switch(value) {
            case "frontier_selected": return "Explorar una frontera del mapa observado.";
            case "exploring_observed_free_space": return "Avanzar por espacio observado libre.";
            case "inspection_observed_lidar": return "Inspeccionar y registrar nuevas lecturas LiDAR.";
            case "memory_skips_optional_inspection": return "El recuerdo permite omitir la inspección opcional.";
            case "return_reserve": return "Regresar conservando margen de misión.";
            case "clearance_changed_return": return "El margen ha cambiado: regresar por espacio observado libre.";
            case "returned_home": return "Base alcanzada. Aterrizaje solicitado.";
            case "sensor_stale": return "Sensores caducados: frenar y aterrizar.";
            default: return value;
        }
    }
}
