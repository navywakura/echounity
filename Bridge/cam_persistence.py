"""Atomic, versioned persistence for the original 4096-slot CAM.

An episode is immutable. Active blocking inferences are a separate, revisable
index. This module does not implement a second CAM or a thought bus.
"""
from dataclasses import asdict
import hashlib
import json
import os
from pathlib import Path

# The existing boundary establishes historical imports, without a language model.
from station_agent import HypothesisAgent  # noqa: F401
from echo5.legacy import module
module('nexus0.encoder')
Cam = module('core.cam').Cam
_qn = module('core.qn')
CAM_SLOTS, simhash16 = _qn.CAM_SLOTS, _qn.simhash16
unpack = module('core.wsp').unpack


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True, allow_nan=False)


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


class CamMemory:
    def __init__(self, path=None):
        self.path = Path(path).resolve() if path else None
        self.cam = Cam()
        self.episodes = {}
        self.active_blocks = {}
        self.serial = 0
        if self.path and self.path.exists():
            self._restore(json.loads(self.path.read_bytes()))

    def payload(self):
        slots = []
        for s in self.cam.slot:
            if not s.occupied:
                slots.append(None)  # canonical representation of a default empty slot
                continue
            row = asdict(s)
            row.update(wsp=s.wsp.hex(), keys=[k.hex() for k in s.keys])
            slots.append(row)
        body = dict(schema='echo.cam.checkpoint.v1', capacity=CAM_SLOTS,
                    used=self.cam.used, destroyed=self.cam.destroyed, serial=self.serial,
                    slots=slots, episodes={str(k): v for k, v in self.episodes.items()},
                    key_index={k.hex(): v for k, v in self.cam.key_index.items()},
                    active_blocks=self.active_blocks)
        return dict(body, sha256=digest(body))

    def _restore(self, data):
        body = dict(data)
        claimed = body.pop('sha256', None)
        if (claimed != digest(body) or body.get('schema') != 'echo.cam.checkpoint.v1'
                or body.get('capacity') != CAM_SLOTS or body.get('destroyed') != 0
                or len(body.get('slots', [])) != CAM_SLOTS):
            raise ValueError('invalid CAM checkpoint integrity/layout')
        if type(body['serial']) is not int or not 0 <= body['serial'] <= 0xFFFFFF:
            raise ValueError('invalid CAM receipt serial')
        self.episodes = {int(k): v for k, v in body['episodes'].items()}
        seen = set()
        for i, row in enumerate(body['slots']):
            if row is None:
                if i in self.episodes:
                    raise ValueError('episode without occupied slot')
                continue
            s = self.cam.slot[i]
            if set(row) != set(asdict(s)) or row['occupied'] != 1 or row['has_fact'] != 1:
                raise ValueError('invalid CAM slot')
            values = dict(row, wsp=bytes.fromhex(row['wsp']), E=tuple(row['E']),
                          keys=[bytes.fromhex(k) for k in row['keys']])
            wsp, e = values['wsp'], values['E']
            episode = self.episodes.get(i)
            if (len(wsp) != 16 or wsp in seen or values['vertex'] != simhash16(wsp)
                    or any(type(n) is not int or not -100 <= n <= 100 for n in e)
                    or tuple(unpack(wsp)['E'].values()) != e or not episode
                    or episode['context'] != episode['receipt']['context']
                    or values['extract'] != canonical(episode['receipt'])
                    or digest(episode['receipt']) != episode['sha256']):
                raise ValueError('invalid CAM episode/WSP')
            for field, value in values.items():
                setattr(s, field, value)
            seen.add(wsp)
        self.cam.used = len(seen)
        if self.cam.used != body['used'] or len(self.episodes) != self.cam.used:
            raise ValueError('invalid CAM occupancy')
        self.cam.key_index = {bytes.fromhex(k): v for k, v in body['key_index'].items()}
        for key, slot in self.cam.key_index.items():
            if type(slot) is not int or slot not in self.episodes or key not in self.cam.slot[slot].keys:
                raise ValueError('invalid CAM exact-key index')
        self.active_blocks = body['active_blocks']
        for key, value in self.active_blocks.items():
            row = self.episodes.get(value['slot'])
            if (not row or canonical(row['context']) != key
                    or row['receipt']['outcome']['status'] != 'blocked'):
                raise ValueError('active inference without blocked receipt')
        self.serial = body['serial']

    def save(self):
        if self.path:
            self.write_snapshot(self.path)

    def write_snapshot(self, path):
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        data = self.payload()
        temporary = path.with_name(path.name + '.tmp')
        with temporary.open('w') as file:
            file.write(canonical(data) + '\n')
            file.flush()
            os.fsync(file.fileno())
        os.replace(temporary, path)
        return data['sha256']

    def store(self, wsp, E, receipt, source, label):
        if self.cam.used >= CAM_SLOTS:
            return -1
        if self.cam.get(wsp) >= 0:
            raise ValueError('refusing to overwrite an immutable episode')
        context = receipt['context']
        slot = self.cam.put(wsp, simhash16(wsp), 0, E=E, has_fact=1,
                            extract=canonical(receipt), source=str(source), label=label,
                            kind='physical_receipt', key=canonical(context).encode())
        if slot >= 0:
            self.episodes[slot] = dict(context=context, receipt=receipt, sha256=digest(receipt))
            self.serial += 1
        return slot
