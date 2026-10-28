"""Fresh-process decision replay from the recorded permitted observations."""
import argparse
from dataclasses import asdict
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import echoai

from echoai.echo58.wspfiles.memory import load_memory


def audit(directory):
    d=Path(directory); cfg=json.loads((d/'configuration.json').read_text())
    root=Path(echoai.__file__).resolve().parent
    for rel, digest in cfg['sources'].items():
        if rel.startswith('echo58/') and hashlib.sha256((root/rel).read_bytes()).hexdigest()!=digest:
            raise ValueError('imported memory custody: '+rel)
    source=d/'sources/core.py'
    expected=cfg['sources']['robotics/flightlab/core.py']
    if hashlib.sha256(source.read_bytes()).hexdigest()!=expected: raise ValueError('core custody')
    spec=importlib.util.spec_from_file_location('flightlab_recorded_core',source)
    core=importlib.util.module_from_spec(spec); sys.modules[spec.name]=core; spec.loader.exec_module(core)
    agent=core.Agent(cfg['arm'], d/'initial-memory.wsp' if cfg['initial_memory'] else None,
                     budget_ms=cfg['seconds']*1000,max_inspections=cfg['inspections'])
    n=0
    for line in (d/'decisions.jsonl').open():
        row=json.loads(line); obs=core.Observation(**row['observation'])
        got=agent.step(obs)
        if got!=row['decision']: raise ValueError('decision mismatch at '+str(n))
        n+=1
    result=json.loads((d/'result.json').read_text())
    if result['status']=='STOP_TECHNICAL':
        return dict(status='PARTIAL_REPLAY',decisions=n,flight_status='STOP_TECHNICAL')
    m=load_memory(d/'memory.wsp')
    if m.records!=agent.memory.memory.records: raise ValueError('memory replay mismatch')
    for key,value in dict(inspections=agent.inspections,skipped_inspections=agent.skips,
                          visited=len(agent.visited),known_cells=len(agent.grid.cells)).items():
        if result[key]!=value: raise ValueError('reported metric mismatch: '+key)
    frame_count=0; previous=-1; maximum_altitude=0
    for line in (d/'frames.jsonl').open():
        f=json.loads(line)
        if f['source']!='Gazebo/PX4 SITL' or f['time']<previous: raise ValueError('frame provenance/order')
        previous=f['time']; frame_count+=1; maximum_altitude=max(maximum_altitude,f['position'][2])
    if n==0 or frame_count==0: raise ValueError('empty evidence')
    return dict(status='PASS_IDENTICAL_REPLAY',decisions=n,frames=frame_count,
                memory_records=len(m.records),maximum_altitude_m=maximum_altitude,
                flight_status=result['status'],returned_home=result['returned_home'],api_calls=0)


if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('directory',type=Path); a=p.parse_args()
    r=audit(a.directory); target=a.directory/'audit.json'
    if target.exists(): raise SystemExit('audit exists; preserve result')
    target.write_text(json.dumps(r,indent=2)+'\n'); print(json.dumps(r,indent=2))
