"""Read-only audit of closed E[6]/CAM traces using only the Python standard library.

Checks receipts and ordering independently of the actor's memory/gate code.
An integrity audit is not an independent sensor or a generalization benchmark.

For an unchanged archive, supply both --recorded-root ORIGINAL_PROJECT and
--archive-root EVIDENCE_ROOT (the directory containing Artifacts), and pass
the archived journal as the positional argument. Recorded references are mapped
lexically; the original tree is never consulted and evidence is never rewritten.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


class _ArchivePaths:
    """Resolve recorded references only inside an explicitly supplied archive."""

    def __init__(self, recorded_root, archive_root):
        # Do not resolve the recorded namespace: it need not exist on this host.
        self.recorded_root = Path(recorded_root)
        if not self.recorded_root.is_absolute() or '..' in self.recorded_root.parts:
            raise ValueError('recorded root must be an absolute path without traversal')
        self.archive_root = Path(archive_root).absolute()
        if self.archive_root.is_relative_to(self.recorded_root):
            raise ValueError('archive root must be outside the recorded namespace')
        self.archive_root = self.archive_root.resolve()
        if self.archive_root.is_relative_to(self.recorded_root):
            raise ValueError('archive root must be outside the recorded namespace')

    def local(self, path):
        path = Path(path)
        if path.absolute().is_relative_to(self.recorded_root):
            raise ValueError('archive path is in the recorded namespace')
        path = path.resolve()
        if not path.is_relative_to(self.archive_root) or path.is_relative_to(self.recorded_root):
            raise ValueError('path escapes archive root')
        return path

    def recorded(self, path):
        path = Path(path)
        if not path.is_absolute() or '..' in path.parts or not path.is_relative_to(self.recorded_root):
            raise ValueError('reference crosses recorded namespace or contains traversal')
        return self.local(self.archive_root / path.relative_to(self.recorded_root))


def encoded(value, ascii=False):
    return json.dumps(value, ensure_ascii=ascii, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def checked_rows(path):
    previous = '0' * 64
    for index, line in enumerate(Path(path).read_bytes().splitlines()):
        row = json.loads(line); claimed = row.pop('hash')
        assert row['index'] == index and row['prev'] == previous
        assert hashlib.sha256(encoded(row)).hexdigest() == claimed
        previous = claimed
        yield row


def restored_slots(event, journal, archive=None):
    checkpoint = archive.recorded(event['checkpoint']) if archive else Path(event['checkpoint'])
    assert checkpoint.resolve().parent == journal.resolve().parent
    data = json.loads(checkpoint.read_bytes()); claimed = data.pop('sha256')
    assert claimed == event['sha256'] == hashlib.sha256(encoded(data, ascii=True)).hexdigest()
    assert len(data['slots']) == 4096 and data['destroyed'] == 0
    result = {}; sources = {}
    for index, value in enumerate(data['slots']):
        if value is None:continue
        row = data['episodes'][str(index)]
        assert value['occupied'] == value['has_fact'] == 1
        assert encoded(row['receipt'], ascii=True).decode() == value['extract']
        assert hashlib.sha256(value['extract'].encode()).hexdigest() == row['sha256']
        source = value['source']
        if source not in sources:
            source_path = archive.recorded(source) if archive else source
            sources[source] = {(r['slot'],r['wsp'],r['extract_sha256']):r
                for r in checked_rows(source_path) if r['kind'] in ('arachnid_cam_write','station_cam_write')}
        original = sources[source][(index,value['wsp'],row['sha256'])]
        assert original['extract'] == row['receipt']
        result[index] = original
    assert len(result) == data['used'] == event['used']
    return result


def audit_station(path):
    observations,decisions,slots,door_slots = {},{},{},{}
    e=(0,20,0,0,10,0);final=None;interventions=0
    for row in checked_rows(path):
        kind=row['kind']
        if kind=='station_observation':observations[row['observation']['seq']]=row['observation']
        elif kind=='station_decision':decisions[row['decision']['seq']]=row['decision']
        elif kind=='station_e6':
            obs=observations[row['seq']];e=row['E'];wsp=bytes.fromhex(row['wsp'])
            assert list(struct.unpack('<BBBBbbbbbbBBBBBB',wsp)[4:10])==e and e[2]==e[5]==0
            block=obs['outcome']['present'] and obs['outcome']['status']=='blocked'
            assert e[4]==(80 if block or obs['battery']<25 else 10)
            assert e[0]==(-80 if block else 40 if obs['completed'] else 0)
        elif kind=='station_cam_write':
            obs=observations[row['seq']];receipt=row['extract'];o=receipt['outcome']
            assert hashlib.sha256(encoded(receipt,ascii=True)).hexdigest()==row['extract_sha256']
            assert row['slot'] not in slots and row['destroyed']==0
            assert list(struct.unpack('<BBBBbbbbbbBBBBBB',bytes.fromhex(row['wsp']))[4:10])==row['E']
            if row['event']=='door_without_key':
                door=receipt['door'];assert door in obs['objects'] and door['kind']=='door' and door['locked']
                assert receipt['inventory']==obs['inventory'] and 'key' not in obs['inventory']
                door_slots[row['slot']]=door
            else:
                assert o==obs['outcome'] and o['present']
                d=decisions[o['seq']]
                for key in ('session','action','objectId'):assert o[key]==d[key]
                assert [o['targetX'],o['targetZ']]==[d['x'],d['z']]
                assert [o['actualX'],o['actualZ']]==[obs['x'],obs['z']]
                if o['action']=='move' and o['status']=='arrived':
                    assert o['distanceMm']<=200 and [d['x'],d['z']]==[obs['x'],obs['z']]
                else:assert o['status'] in ('applied','blocked')
            slots[row['slot']]=row;assert len(slots)==row['used']
        elif kind=='station_door_refuted':
            door=door_slots[row['slot']];obs=observations[row['seq']]
            current=next(o for o in obs['objects'] if o['id']==door['id'])
            assert not current['locked'] or 'key' in obs['inventory']
            assert row['destroyed']==0
        elif kind=='station_gate':
            assert row['cam_slot'] in door_slots and row['actual']=='wait' and row['gate_result']==2
            interventions+=1
        elif kind=='station_basal_finish':final=row['result']
    assert final and final['status']=='closed' and final['resolution']!='unresolved'
    assert final['basal']['camUsed']==len(slots) and final['basal']['camDestroyed']==0
    assert final['basal']['E']==list(e) and final['budgetUsed']+final['budgetRemaining']==1_000_000
    return dict(valid=True,domain='station',journal=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                completed=final['completed'],cam_receipts=len(slots),interventions=interventions,
                budgetUsed=final['budgetUsed'])


def audit(path, *, recorded_root=None, archive_root=None):
    """Audit a journal, optionally mapping recorded references into an archive.

    Both roots are required for relocation; ``path`` names the archived journal.
    Omitting both roots preserves the original one-argument auditing behavior.
    """
    if (recorded_root is None) != (archive_root is None):
        raise ValueError('recorded_root and archive_root must be supplied together')
    archive = _ArchivePaths(recorded_root, archive_root) if recorded_root is not None else None
    path = archive.local(path) if archive else Path(path)
    raw = path.read_bytes()
    if any(json.loads(line)['kind']=='station_observation' for line in raw.splitlines()):
        return audit_station(path)
    observations, decisions, slots, gates = {}, {}, {}, []
    observed_edges, repositions = set(), []
    previous = '0' * 64
    last_e, last_wsp, e_updates, reads, final = (0,20,0,0,10,0), None, 0, 0, None
    for index, line in enumerate(raw.splitlines()):
        row = json.loads(line)
        claimed = row.pop('hash')
        assert row['index'] == index and row['prev'] == previous
        assert row['schema'] == 'echo45-arc-decisions-v1' and row['agent'] == 'echo5'
        assert hashlib.sha256(encoded(row)).hexdigest() == claimed
        previous = claimed
        kind = row['kind']
        if kind == 'arachnid_cam_restore':
            assert not slots
            slots.update(restored_slots(row, path, archive))
        elif kind == 'navigation_observation':
            obs = row['observation']
            observations[obs['seq']] = obs
        elif kind == 'navigation_decision':
            decisions[row['decision']['seq']] = row['decision']
        elif kind == 'navigation_consequence' and row['matched'] and row['confirmed']:
            obs=observations[row['seq']];o=obs['outcome']
            if o['action']=='move':
                assert o['status']=='arrived' and o['distanceMm']<=200
                assert abs(o['x']-o['fromX'])+abs(o['z']-o['fromZ'])==1
                observed_edges.add((obs['session'],o['fromX'],o['fromZ'],o['command'],o['x'],o['z']))
        elif kind == 'navigation_calibration_reposition':
            route=row['route'];positions=route['positions'];actions=route['actions']
            assert route['source']=='confirmed_session_receipts' and len(positions)==len(actions)+1
            obs=observations[row['seq']]
            assert positions[0]==[obs['z'],obs['x']] and positions[-1]==row['sample_origin'][::-1]
            for i,action in enumerate(actions):
                z,x=positions[i];zz,xx=positions[i+1]
                assert (obs['session'],x,z,action,xx,zz) in observed_edges
            repositions.append(row)
        elif kind == 'arachnid_e6':
            obs = observations[row['seq']]
            values = row['E']
            wsp = bytes.fromhex(row['wsp'])
            assert len(wsp) == 16 and list(struct.unpack('<BBBBbbbbbbBBBBBB', wsp)[4:10]) == values
            assert all(type(n) is int and -100 <= n <= 100 for n in values)
            assert values[2] == values[5] == 0
            block = obs['outcome']['present'] and obs['outcome']['status'] == 'blocked'
            expected = 80 if block else max(10, last_e[4]-max(1,(last_e[4]-10+3)//4))
            assert values[4] == expected
            assert values[0] == (-80 if block else 40 if obs['completed'] else 0)
            last_e, last_wsp = tuple(values), wsp
            e_updates += 1
        elif kind == 'arachnid_cam_write':
            obs = observations[row['seq']]
            extract, outcome = row['extract'], obs['outcome']
            assert extract['outcome'] == outcome and extract['sequence'] == obs['seq']
            assert outcome['present']
            decision = decisions[outcome['seq']]
            before = observations[outcome['seq']]
            for key in ('session','command','action'):
                assert outcome[key] == decision[key]
            assert (outcome['fromX'],outcome['fromZ']) == (before['x'],before['z'])
            assert (outcome['x'],outcome['z']) == (obs['x'],obs['z'])
            stationary = (obs['x'],obs['z']) == (before['x'],before['z'])
            if outcome['action'] == 'scan':
                assert stationary and outcome['status'] == 'scanned' and obs['yaw'] == decision['yaw']
            elif outcome['status'] == 'arrived':
                assert abs(obs['x']-before['x'])+abs(obs['z']-before['z']) == 1 and outcome['distanceMm'] <= 200
            else:
                assert outcome['status'] == 'blocked' and stationary
            context=extract['context']
            assert context.get('session',obs['session']) == obs['session'] and context['world'] == obs['world']
            assert hashlib.sha256(encoded(extract, ascii=True)).hexdigest() == row['extract_sha256']
            assert bytes.fromhex(row['wsp']) == last_wsp
            assert row['slot'] not in slots and row['destroyed'] == 0
            slots[row['slot']] = row
            assert row['used'] == len(slots)
        elif kind == 'arachnid_cam_read':
            stored = slots[row['slot']]
            assert stored['extract']['context'] == row['context']
            assert stored['extract_sha256'] == row['extract_sha256'] and row['verified']
            reads += 1
        elif kind == 'arachnid_gate':
            stored = slots[row['cam_slot']]
            assert stored['extract']['outcome']['status'] == 'blocked'
            obs=observations[row['seq']];context=stored['extract']['context']
            assert context['world']==obs['world'] and context['origin']==[obs['x'],obs['z']]
            if row['source'] == 'post_block_wait':
                assert row['E'][4] >= 60 and row['actual'] == 'scan' and row['gate_result'] == 1
            else:
                assert row['source'] == 'repeated_move' and row['actual'] == 'wait' and row['gate_result'] == 2
            gates.append(row)
        elif kind == 'arachnid_inference_retired':
            assert row['slot'] in slots and row['destroyed']==0 and row['reason']=='new_free_ray_measurement'
        elif kind == 'navigation_finish':
            final = row['result']
    assert final is not None and final['status'] == 'closed' and not final['pending'], 'not a closed run'
    assert final['basal']['camUsed'] == len(slots) and final['basal']['camDestroyed'] == 0
    assert final['basal']['E'] == list(last_e) and final['basal']['memoryReads'] == reads
    assert final['budgetUsed'] + final['budgetRemaining'] == 2_000_000
    for gate in gates:
        assert decisions[gate['seq']]['action'] == gate['actual']
    for row in repositions:
        d=decisions[row['seq']]
        assert d['action']=='move' and d['command']==row['route']['actions'][0]
        assert d['support']==row['support']
    return dict(valid=True, journal=str(path.resolve()), sha256=hashlib.sha256(raw).hexdigest(),
                events=index+1, last_hash=previous, e_updates=e_updates, cam_receipts=len(slots),
                verified_reads=reads, interventions=len(gates), repositions=len(repositions), completed=final['completed'],
                budgetUsed=final['budgetUsed'], metrics=final['metrics'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('journal', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--recorded-root', type=Path, help='original absolute project namespace')
    parser.add_argument('--archive-root', type=Path, help='archive directory containing Artifacts')
    args = parser.parse_args()
    if (args.recorded_root is None) != (args.archive_root is None):
        parser.error('--recorded-root and --archive-root must be supplied together')
    result = json.dumps(audit(args.journal, recorded_root=args.recorded_root,
                              archive_root=args.archive_root), indent=2)+'\n'
    if args.output:
        args.output.write_text(result)
    print(result)
