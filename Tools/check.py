"""Serial checks for a closed Unity editor. Long checks run as a detached job."""
import argparse
from datetime import datetime,timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from launch import ECHO,UNITY,running_editor


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--worker',action='store_true')
    parser.add_argument('--integration',action='store_true')
    parser.add_argument('--python-only',action='store_true',help='Bounded bridge tests; do not start Unity')
    args=parser.parse_args()
    if args.python_only and args.integration: parser.error('--python-only excludes --integration')
    if not args.python_only and running_editor(): raise SystemExit('Close this project in Unity before running serial checks.')
    (ROOT/'Logs').mkdir(exist_ok=True)
    if not args.worker:
        with (ROOT/'Logs/check-process.log').open('a') as log:
            command=[sys.executable,'-B',str(Path(__file__).resolve()),'--worker']
            if args.integration: command.append('--integration')
            if args.python_only: command.append('--python-only')
            proc=subprocess.Popen(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
        print('Background checks PID',proc.pid,'log',ROOT/'Logs/check-process.log');return
    sources={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
             for folder in ('Assets/Echo','Bridge','Tools') for p in (ROOT/folder).rglob('*')
             if p.suffix in ('.cs','.py','.sh')}
    receipt={'started':datetime.now(timezone.utc).isoformat(),'sources':sources,'checks':[],'valid':False}
    receipt['scope']='Python bridge only' if args.python_only else 'Python and Unity'
    commands=[('bridge',[sys.executable,'-B','-m','unittest','discover','-s','Bridge/tests','-v'])]
    if not args.python_only:
        commands.append(('unity',[str(UNITY),'-batchmode','-nographics','-projectPath',str(ROOT),'-executeMethod','EchoStation.Editor.StationVerification.RunAll','-quit','-job-worker-count','2','-logFile',str(ROOT/'Logs/verified-editor.log')]))
    if args.integration:
        commands.extend([('start_bridge',[sys.executable,str(ROOT/'Tools/launch.py'),'--bridge-only']),
                         ('integration',[str(UNITY),'-batchmode','-nographics','-projectPath',str(ROOT),'-executeMethod','EchoStation.Editor.StationEditor.RunIntegration','-stationCheck','-job-worker-count','2','-logFile',str(ROOT/'Logs/verified-integration-editor.log')])])
    try:
        for name,command in commands:
            print('RUN',name,flush=True)
            with (ROOT/f'Logs/verified-{name}.log').open('w') as log:
                result=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,timeout=600,env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1'),check=False)
            receipt['checks'].append({'name':name,'exit_code':result.returncode})
            if result.returncode: raise RuntimeError(name+' failed; see Logs')
        for path,digest in sources.items():
            if hashlib.sha256((ROOT/path).read_bytes()).hexdigest()!=digest: raise RuntimeError('Source changed during checks: '+path)
        receipt['valid']=True
    except Exception as exc:
        receipt['error']=str(exc)
    receipt['finished']=datetime.now(timezone.utc).isoformat()
    (ROOT/'Logs/verification.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print('PASS' if receipt['valid'] else 'FAIL',receipt.get('error',''),flush=True)
    return 0 if receipt['valid'] else 1


if __name__=='__main__': raise SystemExit(main())
