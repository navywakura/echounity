"""One-command closure pipeline. Parent returns; --worker keeps durable logs.

Development precedes a single freshly seeded, source-frozen confirmation.
Every run has a unique directory; existing exams are never overwritten.
"""
import argparse
from datetime import datetime,timezone
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import signal
import socket
import subprocess
import sys
import time
import urllib.request

from launch import ROOT,ECHO,UNITY,running_editor
PYTHON=sys.executable


def save(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,indent=2)+'\n')


def sources():
    result={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
        for folder in ('Bridge','Tools','Assets/Echo','Assets/EchoArachnid','Assets/Scenes',
                       'ArtSource/ECHO5_Arachnid','ProjectSettings','Packages')
        for p in (ROOT/folder).rglob('*') if p.suffix in
        ('.py','.cs','.prefab','.unity','.html','.css','.js','.fbx','.blend','.meta','.asset','.json','.txt')}
    # Freeze imported ECHO code too; Unity's adapter alone is not the brain.
    legacy=ECHO.parent
    paths=list((ECHO/'src').rglob('*.py'))+list((legacy/'nexus0').rglob('*.py'))
    paths+=list((legacy/'core').rglob('*.py'))
    for p in paths:result[str(p.resolve())]=hashlib.sha256(p.read_bytes()).hexdigest()
    result['Docs/Arachnid/CLOSURE-1.0-PLAN.md']=hashlib.sha256(
        (ROOT/'Docs/Arachnid/CLOSURE-1.0-PLAN.md').read_bytes()).hexdigest()
    return result


def run(argv,log,timeout=1200):
    with log.open('w') as file:
        return subprocess.run(list(map(str,argv)),cwd=ROOT,stdout=file,stderr=subprocess.STDOUT,
                              timeout=timeout,env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1')).returncode


def spawn(argv,log):
    file=log.open('w')
    p=subprocess.Popen(list(map(str,argv)),cwd=ROOT,stdout=file,stderr=subprocess.STDOUT,
                       env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1'))
    file.close();return p


def stop(proc):
    if proc and proc.poll() is None:
        proc.send_signal(signal.SIGINT)
        try:proc.wait(timeout=15)
        except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15)


def free_port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]


def ready(proc,port):
    for _ in range(200):
        if proc.poll() is not None:raise RuntimeError('bridge exited')
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{port}/health',timeout=.3):return
        except OSError:time.sleep(.1)
    raise RuntimeError('bridge health timeout')


def unity(output,phase,port,dataset=None,conditioning=False):
    command=[UNITY,'-batchmode','-nographics','-projectPath',ROOT,'-executeMethod',
             'EchoStation.Arachnid.Editor.ClosureEditor.Run','-job-worker-count','2',
             '-closureOutput',output,'-closureEndpoint',f'http://127.0.0.1:{port}',
             '-logFile',output/f'unity-{phase}.log']
    command+=['-closureConditioning'] if conditioning else ['-closureCheck','-closurePhase',phase]
    if dataset:command+=['-closureDataset',dataset]
    return command


def conditioning(output):
    folder=output/'conditioning';folder.mkdir()
    port=free_port();root=folder/'brain'
    base=[PYTHON,'-B',ROOT/'Tools/conditioning_check.py','--serve','--root',root,'--port',str(port)]
    brain=spawn(base,folder/'brain-initial.log');ready(brain,port)
    editor=spawn(unity(folder,'conditioning',port,conditioning=True),folder/'editor-process.log')
    handled=set();restarts=[];start=time.monotonic()
    try:
        while editor.poll() is None:
            if time.monotonic()-start>500:raise RuntimeError('conditioning timeout')
            request=folder/'restart-request.txt'
            stage=request.read_text() if request.exists() else ''
            if stage and stage not in handled:
                before=brain.pid;stop(brain)
                if stage=='retention':
                    # Preserve exactly the pre-second-encounter state for the ablation.
                    # The later extinction trial must not weaken the control's memory.
                    control_root=folder/'control-brain';control_root.mkdir()
                    for name in ('cam.json','empirical-motor.json'):
                        shutil.copy2(root/name,control_root/name)
                    save(folder/'paired-control.json',dict(
                        original_cam_sha256=hashlib.sha256((root/'cam.json').read_bytes()).hexdigest(),
                        control_cam_sha256=hashlib.sha256((control_root/'cam.json').read_bytes()).hexdigest(),
                        memory_initial_state_identical=True,only_intervention='memory_control=False'))
                args=(base[:base.index('--root')+1]+[folder/'control-brain']+base[base.index('--root')+2:]+['--no-cam']
                      if stage=='control' else base)
                brain=spawn(args,folder/f'brain-{stage}.log');ready(brain,port)
                restarts.append(dict(stage=stage,old_pid=before,new_pid=brain.pid,port=port))
                (folder/'restart-ready.txt').write_text(stage);handled.add(stage)
            time.sleep(.1)
        save(folder/'restarts.json',restarts)
        return editor.returncode==0 and len(restarts)==2
    finally:stop(editor);stop(brain)


def measured_runs(output,phases,dataset=None):
    folder=output;port=free_port()
    brain=spawn([PYTHON,'-B',ROOT/'Bridge/arachnid_server.py','--port',port,
                 '--artifacts',folder/'brain','--no-persistence'],folder/'brain.log')
    sampler=None
    try:
        ready(brain,port);(folder/'resource-phase.txt').write_text('idle')
        sampler=spawn([PYTHON,'-B',ROOT/'Tools/measure_resources.py','--pid',brain.pid,
            '--output',folder/'resources.json','--phase',folder/'resource-phase.txt','--stop',folder/'resources.stop'],folder/'resources.log')
        time.sleep(2);(folder/'resource-phase.txt').write_text('active')
        results={}
        for phase in phases:
            print('RUN Unity',phase,flush=True)
            results[phase]=run(unity(folder,phase,port,dataset),folder/f'process-{phase}.log',timeout=4200)==0
        with urllib.request.urlopen(f'http://127.0.0.1:{port}/metrics',timeout=5) as response:
            latency=json.load(response);save(folder/'latency.json',latency)
            results['decision_latency_measured']=bool(latency['decision_latency_ms'])
        return results
    finally:
        (folder/'resources.stop').touch()
        if sampler:sampler.wait(timeout=15)
        stop(brain)


def audit_all(output):
    from audit_arachnid_state import audit
    results=[]
    for journal in sorted(output.rglob('thought.jsonl')):
        try:results.append(audit(journal))
        except Exception as exc:results.append(dict(valid=False,journal=str(journal),error=repr(exc)))
    result=dict(valid=bool(results) and all(r['valid'] for r in results),journals=results)
    save(output/'audit-final.json',result);return result['valid']


def worker(output,development_only=False):
    status=dict(started=datetime.now(timezone.utc).isoformat(),valid=False,checks={})
    def record(name,valid):
        status['checks'][name]=bool(valid);save(output/'status.json',status)
        print(name,valid,flush=True)
        return valid
    try:
        if not record('bridge_tests',run([PYTHON,'-B','-m','unittest','discover','-s','Bridge/tests','-v'],output/'tests.log')==0):
            raise RuntimeError('software tests failed')
        if not record('unity_prepare',run([UNITY,'-batchmode','-nographics','-projectPath',ROOT,'-executeMethod',
              'EchoStation.Arachnid.Editor.ClosureEditor.Prepare','-job-worker-count','2','-logFile',output/'prepare-editor.log'],output/'prepare.log')==0):
            raise RuntimeError('Unity compilation/geometry checks failed')
        record('conditioning_physics',conditioning(output))
        dev=output/'development';dev.mkdir()
        for name,valid in measured_runs(dev,('development','station')).items():record(name,valid)
        record('offline',run([PYTHON,'-B',ROOT/'Tools/offline_check.py','--output',output/'offline'],output/'offline.log',timeout=300)==0)
        resources=json.loads((dev/'resources.json').read_bytes());record('resources_below_60mb',resources['below_60mb'])
        if not all(status['checks'].values()):raise RuntimeError('development failed; fresh confirmation not opened')
        if not development_only:
            from make_navigation_worlds import make_case,FAMILIES,encoded
            frozen=sources();save(output/'freeze.json',dict(sources=frozen,minimum_successes=15,total=16,profile='cold'))
            # No candidate-dependent screening: first random seed per family/index.
            seeds=[secrets.randbits(128) for _ in range(16)]
            cases=[make_case(FAMILIES[i%4],s,split='confirmation',index=i) for i,s in enumerate(seeds)]
            confirm=output/'confirmation';confirm.mkdir();dataset=confirm/'worlds.json'
            dataset.write_bytes(encoded(dict(schema='echo.navigation.worlds.v1',cases=cases)))
            save(confirm/'seeds.json',dict(seeds=[str(s) for s in seeds],dataset_sha256=hashlib.sha256(dataset.read_bytes()).hexdigest()))
            for name,valid in measured_runs(confirm,('confirmation',),dataset).items():record(name,valid)
            record('frozen_sources',sources()==frozen)
            record('confirmation_resources',json.loads((confirm/'resources.json').read_bytes())['below_60mb'])
        record('audit',audit_all(output))
        status['valid']=all(status['checks'].values()) and (development_only or 'confirmation' in status['checks'])
    except Exception as exc:
        status['error']=repr(exc);print('FAILED',repr(exc),flush=True)
        record('audit',audit_all(output))
    status['finished']=datetime.now(timezone.utc).isoformat();status['development_only']=development_only
    save(output/'status.json',status)
    print(json.dumps(status,indent=2),flush=True);return 0 if status['valid'] else 1


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--integration',action='store_true')
    p.add_argument('--worker',action='store_true');p.add_argument('--development-only',action='store_true')
    p.add_argument('--output',type=Path);args=p.parse_args()
    if running_editor():raise SystemExit('Close this project in Unity before physical verification.')
    output=args.output or ROOT/'Artifacts/Closure'/datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    output=output.resolve()
    if args.worker:return worker(output,args.development_only)
    output.mkdir(parents=True,exist_ok=False)
    command=[sys.executable,'-B',ROOT/'Tools/check.py','--integration','--worker','--output',output]
    if args.development_only:command.append('--development-only')
    with (output/'process.log').open('w') as log:
        proc=subprocess.Popen(list(map(str,command)),cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    save(output/'process.json',dict(pid=proc.pid,command=list(map(str,command))))
    print(json.dumps(dict(pid=proc.pid,output=str(output)),indent=2))


if __name__=='__main__':raise SystemExit(main())
