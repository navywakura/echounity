"""One sequential development check per declared case; no retries or API."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

from echo_flightlab.core import InspectionMemory

HERE=Path(__file__).resolve().parent
CASES=(('memx','warehouse','memx','none',120),
       ('empty','warehouse','empty','none',120),
       ('cache','warehouse','cache','none',120),
       ('changed','changed','memx','none',120),
       ('sensor-loss','warehouse','memx','sensor_stale',60))


def main():
    p=argparse.ArgumentParser(); p.add_argument('directory',type=Path); p.add_argument('--memory',type=Path,required=True)
    a=p.parse_args(); out=a.directory.resolve(); memory=a.memory.resolve()
    if out.exists(): p.error('preserve previous checks; choose a new directory')
    InspectionMemory('memx',memory)
    out.mkdir(parents=True)
    manifest=dict(memory=str(memory),sha256=hashlib.sha256(memory.read_bytes()).hexdigest(),
                  cases=CASES,api_calls=0,description='descriptive development checks; not a scientific comparison')
    (out/'plan.json').write_text(json.dumps(manifest,indent=2)+'\n')
    results=[]
    for name,variant,arm,fault,seconds in CASES:
        d=out/name
        print('START '+name,flush=True)
        env=dict(os.environ,FLIGHTLAB_WAIT='1')
        subprocess.run(['/bin/sh',str(HERE.parents[1]/'Tools/service.sh'),'--out',str(d),'--variant',variant,
            '--arm',arm,'--fault',fault,'--seconds',str(seconds),'--inspections','4','--memory',str(memory)],
            check=True,env=env)
        # The service has finished including ExecStopPost. Replay is sequential.
        subprocess.run([sys.executable,'-B',str(HERE.parents[1]/'Tools/flightlab.py'),'audit',str(d)],check=True)
        result=json.loads((d/'result.json').read_text())
        results.append(dict(case=name,**result))
        (out/'summary.json').write_text(json.dumps(results,indent=2)+'\n')
        print('RECORDED '+name+' '+result['status'],flush=True)
    return 0


if __name__=='__main__': raise SystemExit(main())
