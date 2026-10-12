"""Read-only projection of the existing thought journal, never an actor input."""
import hashlib
import json
from pathlib import Path
from cam_persistence import canonical


def inspect_trace(path):
    path = Path(path)
    if path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError('journal exceeds inspector limit')
    observations, decisions, slots, gates = {}, {}, {}, {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        kind = row['kind']
        if kind in ('navigation_observation', 'station_observation'):
            obs = row['observation']; observations[obs['seq']] = obs
        elif kind in ('navigation_decision', 'station_decision'):
            decision = row['decision']; decisions[decision['seq']] = decision
        elif kind == 'arachnid_cam_restore':
            checkpoint = Path(row['checkpoint']).resolve()
            if checkpoint.parent != path.resolve().parent:
                raise ValueError('checkpoint outside session directory')
            data = json.loads(checkpoint.read_bytes())
            for s, episode in data['episodes'].items():
                value = data['slots'][int(s)]
                slots[int(s)] = dict(slot=int(s), extract=episode['receipt'],
                    extract_sha256=episode['sha256'], wsp=value['wsp'], source=value['source'])
        elif kind in ('arachnid_cam_write', 'station_cam_write'):
            slots[row['slot']] = dict(row, source=str(path))
        elif kind in ('arachnid_gate', 'station_gate'):
            gates.setdefault(row['seq'], []).append(row)
    steps = []
    for seq, decision in list(decisions.items())[-120:]:
        causes = []
        for gate in gates.get(seq, []):
            stored = slots.get(gate['cam_slot'])
            if stored:
                extract = canonical(stored['extract'])
                causes.append(dict(gate=gate, slot=gate['cam_slot'], extract=stored['extract'],
                    extract_text=extract, context=stored['extract']['context'],
                    context_sha256=hashlib.sha256(canonical(stored['extract']['context']).encode()).hexdigest(),
                    extract_sha256=stored['extract_sha256'], source=stored['source'],
                    verified=hashlib.sha256(extract.encode()).hexdigest()==stored['extract_sha256']))
        steps.append(dict(seq=seq, observation=observations.get(seq), decision=decision, causes=causes))
    return dict(schema='echo.trace.inspector.v1', read_only=True, drives_policy=False,
                journal=str(path), steps=steps, receipts=list(slots.values())[-12:])
