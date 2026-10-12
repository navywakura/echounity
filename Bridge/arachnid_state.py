"""Measured E[6], original CAM and original NEXUS gate for the arachnid only.

The adapter changes the post-block inspection policy, not the motor learner.
Episodes are physical receipts, not predicted destinations or inner monologues.
All evidence stays in the existing Recorder/thought.jsonl. No new bus or LLM.
"""
import hashlib
import json
from pathlib import Path
from cam_persistence import CamMemory

from navigation import NavigationAgent, BudgetExceeded, _point
from echo5.legacy import module

encoder = module('nexus0.encoder')  # establishes the explicit historical import boundary
env = module('nexus0.env')
gates = module('nexus0.gate')
constants = module('nexus0.constants')
Cam = module('core.cam').Cam
_qn = module('core.qn')
CAM_SLOTS, HAM_LIMIT = _qn.CAM_SLOTS, _qn.HAM_LIMIT
hamming16, simhash16 = _qn.hamming16, _qn.simhash16
_wsp = module('core.wsp')
E_NAMES, pack, unpack = _wsp.E_NAMES, _wsp.pack, _wsp.unpack


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True)


class ArachnidAgent(NavigationAgent):
    """Per-session factual memory. Diagnostic reads never advance this state."""
    urgency_control = True
    memory_control = True

    def __init__(self, memory=None, **kwargs):
        super().__init__(**kwargs)
        self.memory = memory if memory is not None else CamMemory()
        self.cam = self.memory.cam
        self.E = (0, 20, 0, 0, 10, 0)
        self.wsp = pack(E=dict(zip(E_NAMES, self.E)))
        self.episodes = self.memory.episodes
        self.recent = list(self.episodes)[-6:]
        self.restored_inferences = set()
        if self.cam.used:
            checkpoint = Path(self.journal_path).with_name('cam-start.json')
            sha = self.memory.write_snapshot(checkpoint)
            self.recorder.write(kind='arachnid_cam_restore', checkpoint=str(checkpoint),
                                sha256=sha, used=self.cam.used, destroyed=self.cam.destroyed)
        self.last_written = -1
        self.last_read = -1
        self.last_gate = 'OK / sin modificación'
        self.last_cause = 'Sin recibos de acción todavía.'
        self.acute_block = False
        self.last_block_slot = -1
        self.memory_reads = self.memory_vetoes = self.urgency_scans = 0
        self.dropped = 0
        self.new_cells = 0

    def _context(self, obs, origin, command):
        if self.memory.path:
            # Only a compatible learned command meaning can reactivate an inference.
            meaning = self._effect(command) if command in range(4) else None
            return dict(domain='navigation', version=1, world=obs['world'],
                        size=[obs['width'], obs['height']], epoch=self.model['epoch'],
                        meaning=list(meaning) if meaning is not None else None,
                        origin=list(origin), command=command)
        return dict(session=obs['session'], world=obs['world'], epoch=self.model['epoch'],
                    origin=list(origin), command=command)

    def _key(self, context):
        # Exact context is checked again below. Never call CAM's fuzzy key fallback.
        return canonical(context).encode()

    def _read(self, slot, context):
        if not self.memory_control or not 0 <= slot < CAM_SLOTS:
            return None
        self.budget.consume('arachnid_cam_read', 1)
        row = self.episodes.get(slot)
        value = self.cam.slot[slot]
        if not row or row['context'] != context or not value.occupied or not value.has_fact:
            return None
        self.budget.consume('arachnid_extract_verify', len(value.extract.encode()))
        if (hashlib.sha256(value.extract.encode()).hexdigest() != row['sha256']
                or value.extract != canonical(row['receipt'])
                or hamming16(simhash16(value.wsp), value.vertex) > HAM_LIMIT):
            return None
        self.memory_reads += 1
        self.last_read = slot
        self.recorder.write(kind='arachnid_cam_read', slot=slot, context=context,
                            extract_sha256=row['sha256'], verified=True)
        return row

    def _blocked_memory(self, obs, command):
        origin = _point(obs)
        # The existing sensor reconciliation retires obsolete blocking inferences.
        # Historical episodes remain in CAM; a refuted inference cannot veto.
        if obs['profile'] == 'no_memory' or (origin, command) not in self.blocked_edges:
            return None
        context = self._context(obs, origin, command)
        active = self.memory.active_blocks.get(canonical(context))
        # A cancelled/unmodeled action may create a planner exclusion, but it
        # must not resurrect an independently refuted historical CAM threat.
        if active is None:
            return None
        slot = active['slot']
        row = self._read(slot, context)
        return row if row and row['receipt']['outcome']['status'] == 'blocked' else None

    def _remember(self, obs):
        # Restore only active, compatible inferences before reconciling NEW sensors.
        for key, value in list(self.memory.active_blocks.items()):
            context = self.episodes[value['slot']]['context']
            if (obs['profile'] != 'no_memory' and key not in self.restored_inferences
                    and context == self._context(obs, tuple(context['origin']), context['command'])):
                edge = value['edge']
                self.blocked_edges[(tuple(edge['origin']), edge['command'])] = dict(edge)
                self.restored_inferences.add(key)
                self.recorder.write(kind='arachnid_inference_restore', session=obs['session'],
                                    seq=obs['seq'], slot=value['slot'], context=context, edge=edge)
        edges_before = dict(self.blocked_edges)
        before = set(self.cells)
        super()._remember(obs)
        self.new_cells = len(set(self.cells) - before)
        for origin, command in edges_before.keys() - self.blocked_edges.keys():
            if obs['profile'] == 'no_memory':
                continue
            context = self._context(obs, origin, command)
            key = canonical(context)
            retired = self.memory.active_blocks.pop(key, None)
            if retired:
                self.recorder.write(kind='arachnid_inference_retired', session=obs['session'],
                                    seq=obs['seq'], slot=retired['slot'], context=context,
                                    reason='new_free_ray_measurement', destroyed=self.cam.destroyed)
                self.memory.save()

    def _ingest(self, obs, *, terminal=False):
        consequence = super()._ingest(obs, terminal=terminal)
        self.acute_block = False
        try:
            self.budget.consume('arachnid_state_update', 6)
            outcome = obs['outcome']
            stationary_block = (consequence['matched'] and outcome['action'] == 'move'
                and outcome['status'] == 'blocked' and self.last_observation is not None
                and _point(obs) == _point(self.last_observation) and self.valid)
            kind = (env.KIND_THREAT if stationary_block else env.KIND_GOAL if obs['completed']
                    else env.KIND_NOVEL if self.new_cells else env.KIND_EMPTY)
            signal = unpack(encoder.encode_cell(obs['seq'], kind))
            e = signal['E']
            # Integer telemetry convention for THIS embodiment, not universal emotions.
            e['C'] = 100 * sum(min(2, n) for n in self.model['support']) // 8
            e['A'] = 80 if stationary_block else min(80, 20 + 3 * self.new_cells)
            # Ceil decay reaches the neutral floor instead of getting stuck above it.
            e['U'] = 80 if stationary_block else max(10, self.E[4] - max(1, (self.E[4]-10+3)//4))
            self.E = tuple(e[name] for name in E_NAMES)
            self.wsp = pack(src=signal['src'], rel=signal['rel'], dst=signal['dst'],
                            time=signal['time'], E=e, generator=5,
                            style=(self.memory.serial >> 16) & 255,
                            slot=(self.memory.serial >> 8) & 255, seq=self.memory.serial & 255)
            self.last_cause = (f"Recibo de bloqueo #{obs['seq']}: U=80; V=-80." if stationary_block
                else f"{self.new_cells} celdas nuevas; soporte motor {sum(min(2,n) for n in self.model['support'])}/8; U decae.")
            self.recorder.write(kind='arachnid_e6', session=obs['session'], seq=obs['seq'],
                                E=list(self.E), wsp=self.wsp.hex(), cause=self.last_cause,
                                unmeasured=['D', 'B'], new_cells=self.new_cells)
            if consequence['matched'] and (consequence['confirmed'] or stationary_block):
                slot = self._store(obs)
                if stationary_block and slot >= 0:
                    self.acute_block, self.last_block_slot = True, slot
        except BudgetExceeded:
            self.valid = False
            self.stopped = 'Presupuesto agotado en E[6]/CAM; se conserva el recibo sin ejecutar otra acción.'
            self.recorder.write(kind='arachnid_budget_exhausted', session=obs['session'], seq=obs['seq'])
        return consequence

    def _store(self, obs):
        if self.cam.used >= CAM_SLOTS:
            self.dropped += 1
            self.recorder.write(kind='arachnid_cam_full', session=obs['session'], seq=obs['seq'])
            return -1  # no eviction and no claim that the missing memory was stored
        previous = self.last_observation
        context = self._context(obs, _point(previous), obs['outcome']['command'])
        receipt = dict(context=context, sequence=obs['seq'], outcome=dict(obs['outcome']))
        extract = canonical(receipt)
        # Conservative logical charge: all possible hash probes plus evidence bytes.
        self.budget.consume('arachnid_cam_write', CAM_SLOTS + len(extract.encode()))
        label = f"#{obs['seq']} {obs['outcome']['action']} / {obs['outcome']['status']}"
        slot = self.memory.store(self.wsp, self.E, receipt, self.journal_path, label)
        if slot < 0:
            self.dropped += 1
            return slot
        row = dict(context=context, receipt=receipt, sha256=hashlib.sha256(extract.encode()).hexdigest())
        self.episodes[slot] = row
        self.last_written = slot
        self.recent = (self.recent + [slot])[-6:]
        self.recorder.write(kind='arachnid_cam_write', session=obs['session'], seq=obs['seq'],
                            slot=slot, wsp=self.wsp.hex(), extract=receipt,
                            extract_sha256=row['sha256'], used=self.cam.used, destroyed=self.cam.destroyed)
        if obs['outcome']['status'] == 'blocked':
            edge = self.blocked_edges.get((_point(previous), obs['outcome']['command']))
            if edge:
                self.memory.active_blocks[canonical(context)] = dict(slot=slot, edge=dict(edge))
                self.restored_inferences.add(canonical(context))
        self.memory.save()
        return slot

    def _choose(self, obs):
        self.last_gate = 'OK / sin modificación'
        if (not self.stopped and not obs['completed'] and self.acute_block
                and self.urgency_control and obs['profile'] != 'no_memory'):
            command = obs['outcome']['command']
            memory = self._blocked_memory(obs, command)
            if memory:
                result, action, flags = gates.gate(self.wsp, cam_hit=True, extract_ok=True,
                    E=self.E, proposed=constants.ACTION_WAIT, threat=True)
                if action == constants.ACTION_AVOID:
                    # AVOID is mapped to sensor inspection, never to an invented motor command.
                    self.budget.consume('arachnid_urgency_inspection', 1)
                    self.urgency_scans += 1
                    self.last_gate = f'MODIFY / U={self.E[4]} + CAM #{self.last_read} → inspeccionar'
                    self.recorder.write(kind='arachnid_gate', session=obs['session'], seq=obs['seq'],
                        source='post_block_wait', proposed='wait', actual='scan', E=list(self.E),
                        cam_slot=self.last_read, gate_result=result, flags=flags)
                    return self._decision(obs, 'scan', self.last_gate, yaw=(obs['yaw']+90)%360)
        return super()._choose(obs)

    def _decision(self, obs, action, reason, *, command=-1, yaw=None, route=None):
        effective_command = route['actions'][0] if route is not None else command
        if action == 'move' and not self.stopped:
            memory = self._blocked_memory(obs, effective_command)
            if memory:
                result, gated, flags = gates.gate(self.wsp, cam_hit=True, extract_ok=True,
                    E=self.E, proposed=constants.ACTION_APPROACH, inhibit=True)
                if gated == constants.ACTION_WAIT:
                    self.memory_vetoes += 1
                    self.last_gate = f'BLOCK / CAM #{self.last_read}: intento fallido no refutado'
                    self.recorder.write(kind='arachnid_gate', session=obs['session'], seq=obs['seq'],
                        source='repeated_move', command=effective_command, proposed='move', actual='wait',
                        cam_slot=self.last_read, E=list(self.E), gate_result=result, flags=flags)
                    action, command, route, reason = 'wait', -1, None, self.last_gate
        result = super()._decision(obs, action, reason, command=command, yaw=yaw, route=route)
        result['basal'] = self.basal_snapshot()
        return result

    def basal_snapshot(self):
        episodes = []
        for slot in reversed(self.recent):
            value, row = self.cam.slot[slot], self.episodes[slot]
            o = row['receipt']['outcome']
            episodes.append(dict(slot=slot, label=value.label, sequence=row['receipt']['sequence'],
                verified=bool(value.has_fact), extractHash=row['sha256'],
                summary=f"({o['fromX']},{o['fromZ']}) → ({o['x']},{o['z']}); comando {o['command']}; {o['status']}"))
        return dict(schema='echo.arachnid.basal.v1', available=True, E=list(self.E), wsp=self.wsp.hex(),
            cause=self.last_cause, gate=self.last_gate, camUsed=self.cam.used, camCapacity=CAM_SLOTS,
            camDestroyed=self.cam.destroyed, camDropped=self.dropped, lastWritten=self.last_written,
            lastRead=self.last_read, memoryReads=self.memory_reads, memoryVetoes=self.memory_vetoes,
            urgencyScans=self.urgency_scans, episodes=episodes,
            scope=('CAM persistente' if self.memory.path else 'CAM de esta sesión') +
                  '; D/B sin transductor físico asignado; C es soporte motor, no probabilidad.',
            urgencyControl=self.urgency_control, memoryControl=self.memory_control)

    def finish(self, obs, *, same_observation=False):
        result = super().finish(obs, same_observation=same_observation)
        result['basal'] = self.basal_snapshot()
        return result
