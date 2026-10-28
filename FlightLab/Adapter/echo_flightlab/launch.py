"""Local entry point. A flight never connects to external MAVLink endpoints."""
import argparse
import fcntl
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import signal
import subprocess
import time
import echoai

from echo_flightlab.world import prepare

HERE=Path(__file__).resolve().parent
ROOT=Path(echoai.__file__).resolve().parent
FLIGHTLAB=HERE.parents[1]
IMAGE='localhost/echoai-flight1s-px4:v1.15.4-gpsaccuracy'


def main():
    p=argparse.ArgumentParser()
    p.add_argument('mode',choices=['run','doctor','world','cleanup'])
    p.add_argument('--out',type=Path)
    p.add_argument('--arm',choices=['memx','empty','cache'],default='memx')
    p.add_argument('--variant',choices=['warehouse','empty','changed'],default='warehouse')
    p.add_argument('--seconds',type=int,default=120); p.add_argument('--inspections',type=int,default=8)
    p.add_argument('--memory',type=Path); p.add_argument('--fault',default='none',choices=['none','sensor_stale'])
    p.add_argument('--container')
    a=p.parse_args()
    if a.container and not re.fullmatch(r'echo-flightlab-[a-z0-9-]+',a.container): p.error('container name')
    if a.mode=='cleanup':
        if not a.container: p.error('--container required')
        cleanup_owned(a.container); return 0
    if a.mode=='doctor':
        r=subprocess.run(['podman','image','inspect',IMAGE,'--format','{{.Id}}'],capture_output=True,text=True)
        print(json.dumps(dict(image=IMAGE,image_id=r.stdout.strip(),available=r.returncode==0,
            unity=os.environ.get('UNITY_EDITOR','Unity 6000.0.84f1'),
            cpu_limit=1.5,memory_limit='2GiB',swap=0,external_network=False),indent=2))
        return r.returncode
    if not a.out: p.error('--out required')
    out=a.out.resolve()
    if out.exists(): p.error('output exists; preserve old flight and use a new directory')
    if not 15<=a.seconds<=300 or not 1<=a.inspections<=30: p.error('bounded duration/inspection count')
    lock=open('/tmp/echo58-heavy.lock','a')
    try: fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    except BlockingIOError: raise SystemExit('Another heavy ECHO job is active; no launch.')
    available=int(next(x.split()[1] for x in Path('/proc/meminfo').read_text().splitlines() if x.startswith('MemAvailable:')))*1024
    if available<3500*1024**2: raise SystemExit('Need 3.5 GiB MemAvailable before launching flight.')
    active=subprocess.check_output(['podman','ps','--filter','label=echoai.study=flightlab-r1','--format','{{.Names}}'],text=True).strip()
    if active: raise SystemExit('A Flight Lab container is already running; inspect it before another launch.')
    out.mkdir(parents=True); scenario=out/'scenario'; prepare(scenario,a.variant)
    if a.mode=='world': return 0
    if a.memory:
        from echoai.echo58.wspfiles.memory import load_memory
        load_memory(a.memory); shutil.copyfile(a.memory,out/'initial-memory.wsp')
    image_id=subprocess.check_output(['podman','image','inspect',IMAGE,'--format','{{.Id}}'],text=True).strip()
    name=a.container or 'echo-flightlab-'+hashlib.sha256(str(out).encode()).hexdigest()[:12]
    cmd=['podman','run','--rm','--pull=never','--name',name,'--label','echoai.study=flightlab-r1',
         '--network=none','--security-opt','label=disable','--cpus=1.5','--memory=2g','--memory-swap=2g',
         '--pids-limit=160','--shm-size=128m','-e','ECHOAI_SOURCE=/echoai','-e','PYTHONDONTWRITEBYTECODE=1',
         '-e','OMP_NUM_THREADS=1','-e','OPENBLAS_NUM_THREADS=1','-e','LP_NUM_THREADS=1',
         '-v',str(ROOT)+':/echoai:ro','-v',str(FLIGHTLAB)+':/flightlab:ro','-v',str(scenario)+':/scenario:ro','-v',str(out)+':/out',
         '--entrypoint','python3',image_id,'-B','/flightlab/Tools/flightlab.py','flight',
         '--arm',a.arm,'--seconds',str(a.seconds),'--inspections',str(a.inspections),'--fault',a.fault]
    if a.memory: cmd+=['--memory','/out/initial-memory.wsp']
    config=dict(image=image_id,arm=a.arm,variant=a.variant,seconds=a.seconds,inspections=a.inspections,
                fault=a.fault,network='none',resources=dict(cpu=1.5,ram_bytes=2*1024**3,swap=0),
                initial_memory=bool(a.memory),started_unix=time.time(),container=name)
    source_paths=list(HERE.glob('*.py'))+[ROOT/'echo58/memx/memory.py',ROOT/'echo58/wspfiles/codec.py',ROOT/'echo58/wspfiles/memory.py',
        ROOT/'nexus0/pattern1r_v4/assets/flightrunner_survey.py']
    config['sources']={('robotics/flightlab/'+x.name if x.parent==HERE else str(x.relative_to(ROOT))):hashlib.sha256(x.read_bytes()).hexdigest() for x in source_paths}
    (out/'sources').mkdir()
    for x in HERE.glob('*.py'): shutil.copyfile(x,out/'sources'/x.name)
    (out/'configuration.json').write_text(json.dumps(config,indent=2)+'\n')
    log=(out/'console.log').open('x')
    start=time.monotonic()
    def interrupted(signum,frame): raise KeyboardInterrupt('flight supervisor interrupted')
    signal.signal(signal.SIGTERM,interrupted)
    try:
        proc=subprocess.Popen(cmd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        for line in proc.stdout: print(line,end='',flush=True); log.write(line); log.flush()
        rc=proc.wait()
    finally:
        # Mutate only the exact container created by this invocation.
        cleanup_owned(name)
        log.close()
    (out/'process.json').write_text(json.dumps(dict(returncode=rc,wall_s=time.monotonic()-start))+'\n')
    return rc


def cleanup_owned(name):
    q=subprocess.run(['podman','inspect',name,'--format','{{index .Config.Labels "echoai.study"}}'],capture_output=True,text=True,timeout=5)
    if q.returncode==0 and q.stdout.strip()=='flightlab-r1':
        subprocess.run(['podman','stop','--ignore','--time','2',name],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=10)


if __name__=='__main__': raise SystemExit(main())
