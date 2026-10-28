"""Offline judge of recorded physical takeoff, separate from decision replay.

Uses Gazebo truth only after the flight. It never supplies data to Agent.
Threshold: >=0.8 m observed ascent and >=3 accumulated seconds of telemetry
above that height; gaps over one second do not contribute.
"""
import argparse
import json
from pathlib import Path


def evidence(frames,contact_messages,floor_contacts):
    first=previous=None; maximum=None; duration=0
    for f in frames:
        t,z=f['time'],f['position'][2]
        if first is None: first=z; maximum=z
        maximum=max(maximum,z)
        if previous is not None:
            pt,pz=previous
            if 0<t-pt<=1 and min(z,pz)-first>=.8: duration+=t-pt
        previous=t,z
    ascent=0 if first is None else maximum-first
    return dict(observed_ascent_m=round(ascent,4),airborne_observed_s=round(duration,3),
                takeoff_verified=ascent>=.8 and duration>=3,
                contact_channel_verified=contact_messages>0 and floor_contacts>0)


def audit(directory):
    d=Path(directory); result=json.loads((d/'result.json').read_text())
    with (d/'frames.jsonl').open() as f:
        r=evidence((json.loads(line) for line in f),result.get('contact_messages',0),result.get('floor_contacts',0))
    status=result['status']
    if status=='MISSION_COMPLETE' and not r['takeoff_verified']: status='TAKEOFF_UNCONFIRMED'
    return dict(**r,reported_mission_status=result['status'],physical_mission_status=status,
                observed_obstacle_contact_samples=result.get('contacts'),
                scope='recorded simulated flight; not a resimulation or hardware safety certificate')


if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('directory',type=Path); a=p.parse_args()
    r=audit(a.directory); output=a.directory/'physical-audit.json'
    with output.open('x') as f: f.write(json.dumps(r,indent=2)+'\n')
    print(json.dumps(r,indent=2))
