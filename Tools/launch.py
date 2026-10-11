"""Start only this project's bridge/editor; reuse an existing healthy bridge."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import shutil
import sys
import time
import urllib.error
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from Bridge.echo_source import source_root
ECHO=source_root()
UNITY=Path(os.environ.get('UNITY_EDITOR') or shutil.which('Unity') or 'Unity')


def running_editor():
    if not Path('/proc').is_dir(): return None
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit(): continue
        try:
            cmd=(proc/'cmdline').read_bytes().split(b'\0')
            if str(UNITY).encode() in cmd and str(ROOT).encode() in cmd: return int(proc.name)
        except OSError: pass


def health():
    try:
        with urllib.request.urlopen('http://127.0.0.1:8770/health',timeout=1) as reply: return json.load(reply)
    except (OSError,ValueError,urllib.error.URLError): return None


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--bridge-only',action='store_true')
    parser.add_argument('--classic',action='store_true',help='Open the earlier scripted station missions')
    args=parser.parse_args()
    (ROOT/'Logs').mkdir(exist_ok=True)
    state=health()
    if not state:
        python=sys.executable
        env=dict(os.environ,ECHO5_SOURCE=str(ECHO),PYTHONDONTWRITEBYTECODE='1')
        with (ROOT/'Logs/bridge-process.log').open('a') as log:
            process=subprocess.Popen([str(python),'-B',str(ROOT/'Bridge/server.py')],cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
        (ROOT/'Logs/bridge.pid').write_text(str(process.pid)+'\n')
        for _ in range(50):
            state=health()
            if state: break
            if process.poll() is not None: raise SystemExit('Bridge failed; see Logs/bridge-process.log')
            time.sleep(.1)
        if not state: raise SystemExit('Bridge did not become healthy')
    print('Local bridge:',state)
    if args.bridge_only: return
    pid=running_editor()
    if pid: print('Unity already open for this project, PID',pid);return
    if not UNITY.exists(): raise SystemExit('Set UNITY_EDITOR to the installed Unity executable')
    with (ROOT/'Logs/editor-process.log').open('a') as log:
        method='EchoStation.Editor.StationEditor.Open' if args.classic else 'EchoStation.Editor.NavigationEditor.Open'
        capture='-stationCapture' if args.classic else '-navigationCapture'
        process=subprocess.Popen([str(UNITY),'-projectPath',str(ROOT),'-executeMethod',method,capture,'-job-worker-count','2','-logFile',str(ROOT/'Logs/editor.log')],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    (ROOT/'Logs/editor.pid').write_text(str(process.pid)+'\n')
    print('Unity opening, PID',process.pid)


if __name__=='__main__': main()
