"""Launch the arachnid bridge, Unity (workspace 2) and Blender (workspace 3).

Reuses only processes belonging to this project. Does not stop other services.
"""
import argparse
from datetime import datetime
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import time
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Bridge'))
from echo_source import load_core

def process_with(part,project=None):
    if not Path('/proc').is_dir():return None
    for folder in Path('/proc').iterdir():
        if not folder.name.isdigit():continue
        try:
            argv=[x.decode() for x in (folder/'cmdline').read_bytes().split(b'\0') if x]
            if not any(part in a for a in argv):continue
            if project and str(project) not in argv and (folder/'cwd').resolve()!=project:continue
            return int(folder.name)
        except (OSError,UnicodeError):continue
    return None

def health():
    try:
        with urllib.request.urlopen('http://127.0.0.1:8771/health',timeout=1) as reply:return json.load(reply)
    except (OSError,ValueError):return None

def desktop(argv,workspace):
    if shutil.which('hyprctl') and os.environ.get('HYPRLAND_INSTANCE_SIGNATURE'):
        subprocess.run(['hyprctl','dispatch','exec',f'[workspace {workspace} silent] '+shlex.join(argv)],check=True)
    else:
        subprocess.Popen(argv,cwd=ROOT,start_new_session=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bridge-only',action='store_true')
    parser.add_argument('--no-blender',action='store_true')
    parser.add_argument('--station',action='store_true',help='Open the three Station missions with the arachnid')
    args=parser.parse_args()
    source=load_core()
    unity=Path(os.environ.get('UNITY_EDITOR') or shutil.which('Unity') or 'Unity')
    blender=Path(os.environ.get('BLENDER_EDITOR') or shutil.which('blender') or 'blender')
    logs=ROOT/'Logs/Arachnid';logs.mkdir(parents=True,exist_ok=True)
    alive=health();pid=process_with('arachnid_server.py',ROOT)
    if alive and not pid:raise SystemExit('Port 8771 belongs to another service; no process was stopped.')
    if not alive:
        if pid:raise SystemExit(f'Existing arachnid bridge PID {pid} is not healthy; inspect Logs/Arachnid.')
        python=sys.executable
        stamp=datetime.now().strftime('%Y%m%d-%H%M%S-%f')
        # Stable store across bridge restarts; each session still gets a unique journal.
        artifacts=ROOT/'Artifacts/Arachnid/runtime-v1'
        logfile=logs/('bridge-'+stamp+'.log')
        env=dict(os.environ,ECHO5_SOURCE=str(source),PYTHONDONTWRITEBYTECODE='1')
        with logfile.open('w') as log:
            proc=subprocess.Popen([str(python),'-B',str(ROOT/'Bridge/arachnid_server.py'),
                 '--port','8771','--artifacts',str(artifacts)],cwd=ROOT,env=env,
                 stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
        pid=proc.pid;(logs/'bridge.pid').write_text(str(pid)+'\n')
        for _ in range(60):
            alive=health()
            if alive:break
            if proc.poll() is not None:raise SystemExit('Bridge exited; see '+str(logfile))
            time.sleep(.1)
        if not alive:raise SystemExit('Bridge did not become healthy; see '+str(logfile))
        (logs/'latest-bridge.json').write_text(json.dumps({'pid':pid,'log':str(logfile),'artifacts':str(artifacts)},indent=2)+'\n')
    print('Arachnid bridge ready, PID',pid)
    if args.bridge_only:return
    if process_with(str(unity),ROOT):
        print('Unity already open. Use ECHO > Arácnido > Abrir laboratorio outside Play mode.')
    else:
        if not unity.exists():raise SystemExit('Set UNITY_EDITOR to the Unity executable.')
        log=logs/('editor-'+datetime.now().strftime('%Y%m%d-%H%M%S')+'.log')
        method='EchoStation.Editor.StationEditor.Open' if args.station else 'EchoStation.Arachnid.Editor.ArachnidEditor.Open'
        desktop([str(unity),'-projectPath',str(ROOT),'-executeMethod',method,
                 '-job-worker-count','2','-logFile',str(log)],2)
        print('Opening Unity on workspace 2; log:',log)
    blend=ROOT/'ArtSource/ECHO5_Arachnid/ECHO5_Arachnid.blend'
    if not args.no_blender and not process_with(str(blend)):
        if not blender.exists():raise SystemExit('Set BLENDER_EDITOR to Blender 4.5 LTS.')
        desktop([str(blender),'--factory-startup',str(blend)],3)
        print('Opening Blender on workspace 3.')

if __name__=='__main__':main()
