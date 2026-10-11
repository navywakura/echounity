"""Detached verification of the learned navigation integration (development only)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
from launch import ECHO, UNITY
PYTHON = sys.executable


def source_manifest():
    sys.path.insert(0,str(ROOT))
    from Tools.nav_benchmark import navigation_api, source_files, DEFAULT_WORLDS
    navigation_api()
    inherited=source_files()
    paths = list((ROOT/'Assets/Echo').rglob('*.cs'))
    paths += list((ROOT/'Bridge').glob('*.py'))
    paths += list((ROOT/'Bridge/tests').glob('*.py'))
    paths += list((ROOT/'Tools').glob('*.py'))
    paths += [DEFAULT_WORLDS]
    inherited.update({str(p.relative_to(ROOT)):p for p in paths})
    return {name:{'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for name,p in sorted(inherited.items())}


def fresh_bridge(root):
    from launch import health
    # Only replace this project's known bridge; never kill an unrelated port owner.
    for directory in Path('/proc').iterdir():
        if not directory.name.isdecimal():continue
        try:
            argv=(directory/'cmdline').read_bytes().split(b'\0')
            if str(ROOT/'Bridge/server.py').encode() in argv:
                os.kill(int(directory.name),signal.SIGTERM)
        except (FileNotFoundError,ProcessLookupError,PermissionError):pass
    for _ in range(30):
        if not health():break
        time.sleep(.1)
    if health():raise RuntimeError('Port 8770 is still owned by another live service')
    if root.exists():raise RuntimeError('Physical acquisition must start with a fresh artifact directory')
    root.mkdir(parents=True)
    command=[str(PYTHON),'-B',str(ROOT/'Bridge/server.py'),'--navigation-root',str(root)]
    with (ROOT/'Logs/bridge-process.log').open('a') as log:
        process=subprocess.Popen(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,start_new_session=True,
            env=dict(os.environ,ECHO5_SOURCE=str(ECHO),PYTHONDONTWRITEBYTECODE='1'))
    (ROOT/'Logs/bridge.pid').write_text(str(process.pid)+'\n')
    for _ in range(50):
        if health():return {'pid':process.pid,'command':command,'modelInitiallyAbsent':not (root/'model.json').exists()}
        if process.poll() is not None:raise RuntimeError('Fresh bridge exited')
        time.sleep(.1)
    raise RuntimeError('Fresh bridge did not become healthy')


def worker(integration):
    from launch import running_editor, health
    if running_editor():
        raise RuntimeError('Close this Unity project before verification')
    sources=source_manifest()
    receipt={'valid':False,'started':time.time(),'sources':sources,'steps':[]}
    target=ROOT/'Logs/navigation-verification.json'
    def save():
        temporary=target.with_suffix('.tmp');temporary.write_text(json.dumps(receipt,indent=2)+'\n');temporary.replace(target)
    def run(name,command):
        started=time.time()
        with (ROOT/f'Logs/navigation-{name}.log').open('w') as log:
            p=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,timeout=2400 if name=='integration' else 600,
                env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1',ECHO5_SOURCE=str(ECHO),DISPLAY=os.environ.get('DISPLAY',':0')))
        receipt['steps'].append({'name':name,'code':p.returncode,'seconds':time.time()-started});save()
        if p.returncode: raise RuntimeError(name+' failed')
    save()
    try:
        run('python',[str(PYTHON),'-B','-m','unittest','discover','-s','Bridge/tests','-v'])
        base=[str(UNITY),'-batchmode','-force-glcore','-projectPath',str(ROOT),'-job-worker-count','2']
        run('unity',base+['-executeMethod','EchoStation.Editor.NavigationEditor.RunChecks','-quit','-logFile',str(ROOT/'Logs/navigation-checks-editor.log')])
        if integration:
            physical=ROOT/'Artifacts/NavigationPhysical'/('run-'+time.strftime('%Y%m%dT%H%M%S')+'-'+uuid.uuid4().hex[:8])
            receipt['physicalRoot']=str(physical);receipt['bridge']=fresh_bridge(physical);save()
            run('integration',base+['-executeMethod','EchoStation.Editor.NavigationEditor.RunIntegration','-navigationCheck','-logFile',str(ROOT/'Logs/navigation-integration-editor.log')])
            run('physical-audit',[str(PYTHON),'-B','Tools/audit_physical_navigation.py','--root',str(physical),
                '--output',str(ROOT/'Logs/navigation-physical-audit.json')])
            run('audit-unit',[str(PYTHON),'-B','Tools/test_audit_physical_navigation.py','--root',str(physical)])
        receipt['sourcesUnchanged']=sources==source_manifest()
        receipt['valid']=receipt['sourcesUnchanged']
    except Exception as error:
        receipt['error']=str(error)
    finally:
        receipt['finished']=time.time();save()
    return 0 if receipt['valid'] else 1


def main():
    p=argparse.ArgumentParser();p.add_argument('--worker',action='store_true');p.add_argument('--integration',action='store_true');args=p.parse_args()
    (ROOT/'Logs').mkdir(exist_ok=True)
    if args.worker:return worker(args.integration)
    command=[str(PYTHON),'-B',str(Path(__file__).resolve()),'--worker']
    if args.integration:command.append('--integration')
    with (ROOT/'Logs/navigation-verification-process.log').open('w') as log:
        process=subprocess.Popen(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    (ROOT/'Logs/navigation-verification.pid').write_text(str(process.pid)+'\n')
    print(json.dumps({'pid':process.pid,'receipt':str(ROOT/'Logs/navigation-verification.json')}))
    return 0


if __name__=='__main__':raise SystemExit(main())
