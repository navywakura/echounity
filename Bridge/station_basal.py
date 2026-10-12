"""E[6]/CAM adapter for public Station observations and matched action receipts."""
from station_agent import StationAgent, BudgetExceeded
from arachnid_state import encoder, env, gates, constants, pack, unpack, E_NAMES
from cam_persistence import CamMemory, canonical


class BasalStationAgent(StationAgent):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.memory = CamMemory()
        self.E = (0, 20, 0, 0, 10, 0)
        self.wsp = pack(E=dict(zip(E_NAMES, self.E)))
        self.last_seq = -1
        self.last_gate = 'OK / sin modificación'
        self.last_cause = 'Esperando observación de la estación.'
        self.recent = []
        self.doors = {}
        self.vetoes = 0

    def _record(self, obs, receipt, event):
        self.budget.consume('station_cam_write', 4096 + len(canonical(receipt)))
        slot = self.memory.store(self.wsp, self.E, receipt, self.journal_path, event)
        if slot >= 0:
            self.recent = (self.recent + [slot])[-6:]
            self.recorder.write(kind='station_cam_write', session=obs['session'], seq=obs['seq'],
                                slot=slot, wsp=self.wsp.hex(), E=list(self.E), event=event,
                                extract=receipt, extract_sha256=self.memory.episodes[slot]['sha256'],
                                used=self.memory.cam.used, destroyed=0)
        return slot

    def _signal(self, obs, blocked):
        kind = env.KIND_THREAT if blocked else env.KIND_GOAL if obs['completed'] else env.KIND_EMPTY
        signal = unpack(encoder.encode_cell(obs['seq'], kind))
        e = signal['E']
        e.update(U=max(80 if blocked else 10, 80 if obs['battery'] < 25 else 10), D=0, B=0)
        self.E = tuple(e[n] for n in E_NAMES)
        serial = self.memory.serial
        self.wsp = pack(src=signal['src'], rel=signal['rel'], dst=signal['dst'], time=signal['time'],
                        E=e, generator=5, style=(serial >> 16)&255, slot=(serial >> 8)&255, seq=serial&255)

    def _consequence(self, obs):
        result = super()._consequence(obs)
        if obs['seq'] == self.last_seq:
            return result
        self.last_seq = obs['seq']
        blocked = result['matched'] and obs['outcome']['status'] == 'blocked'
        self._signal(obs, blocked)
        self.last_cause = ('Misión confirmada: V=+40.' if obs['completed'] else
                           'Batería observada por debajo de 25 %: U=80.' if obs['battery'] < 25 else
                           'Recibo de bloqueo: U=80.' if blocked else 'Carga y consecuencias observadas.')
        self.recorder.write(kind='station_e6', session=obs['session'], seq=obs['seq'],
                            E=list(self.E), wsp=self.wsp.hex(), cause=self.last_cause, unmeasured=['D','B'])
        try:
            self.budget.consume('station_basal_update', 6)
            if result['matched'] and (result['confirmed'] or blocked):
                context = dict(domain='station', session=obs['session'], mission=obs['mission'],
                               origin=[self.last_observation['x'], self.last_observation['z']],
                               action=obs['outcome']['action'], objectId=obs['outcome']['objectId'])
                self._record(obs, dict(context=context, sequence=obs['seq'], outcome=obs['outcome']), 'action_receipt')
            for door in (o for o in obs['objects'] if o['kind'] == 'door'):
                locked = door['locked'] and 'key' not in obs['inventory']
                if locked and door['id'] not in self.doors:
                    self._signal(obs, blocked)  # a new immutable WSP id for this observation fact
                    context = dict(domain='station-door', session=obs['session'], objectId=door['id'],
                                   origin=[door['x'], door['z']])
                    receipt = dict(context=context, sequence=obs['seq'], door=dict(door),
                                   inventory=list(obs['inventory']), outcome=dict(status='observed_locked'))
                    self.doors[door['id']] = self._record(obs, receipt, 'door_without_key')
                elif not locked and door['id'] in self.doors:
                    slot = self.doors.pop(door['id'])
                    self.recorder.write(kind='station_door_refuted', seq=obs['seq'], slot=slot,
                                        objectId=door['id'], reason='key_or_open_door_observed', destroyed=0)
        except BudgetExceeded:
            self.stopped = True
            self.recorder.write(kind='station_basal_budget_exhausted', seq=obs['seq'])
        return result

    def _decision(self, obs, action, phase, reason, *, item=None, route=None):
        self.last_gate = 'OK / sin modificación'
        if action == 'interact' and item and item['id'] in self.doors:
            slot = self.doors[item['id']]
            row = self.memory.episodes.get(slot)
            if row and item['locked'] and 'key' not in obs['inventory']:
                result, actual, flags = gates.gate(self.wsp, cam_hit=True, extract_ok=True,
                    E=self.E, proposed=constants.ACTION_APPROACH, inhibit=True)
                if actual == constants.ACTION_WAIT:
                    action, phase, reason = 'wait', 'temporary_door_veto', 'Puerta observada cerrada; falta tarjeta.'
                    self.last_gate = f'BLOCK / CAM #{slot}: puerta temporal'
                    self.vetoes += 1
                    self.recorder.write(kind='station_gate', seq=obs['seq'], cam_slot=slot,
                                        actual='wait', gate_result=result, flags=flags)
        result = super()._decision(obs, action, phase, reason, item=item, route=route)
        result['basal'] = self.basal_snapshot()
        return result

    def basal_snapshot(self):
        return dict(schema='echo.arachnid.basal.v1', available=True, E=list(self.E), wsp=self.wsp.hex(),
                    cause=self.last_cause, gate=self.last_gate, camUsed=self.memory.cam.used,
                    camCapacity=4096, camDestroyed=0, camDropped=0, memoryReads=0,
                    memoryVetoes=self.vetoes, urgencyScans=0, lastWritten=self.recent[-1] if self.recent else -1,
                    lastRead=-1, episodes=[dict(slot=s, sequence=self.memory.episodes[s]['receipt']['sequence'],
                        verified=True, label=self.memory.cam.slot[s].label,
                        extractHash=self.memory.episodes[s]['sha256'], summary=self.memory.cam.slot[s].label)
                        for s in reversed(self.recent)],
                    scope='Estación: reglas de misión declaradas; D/B sin transductor físico asignado.')

    def finish(self, obs):
        result = super().finish(obs)
        result.update(basal=self.basal_snapshot(), completed=obs['completed'],
                      budgetUsed=self.budget.used, budgetRemaining=self.budget.remaining)
        self.recorder.write(kind='station_basal_finish', result=result)
        return result
