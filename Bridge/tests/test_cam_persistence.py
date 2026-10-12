import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from arachnid_state import ArachnidAgent, canonical
from cam_persistence import CamMemory, digest
from navigation import NavigationService, _point
from test_navigation import CameraWorld, encoded, observation, empty_outcome, receipt


class PersistenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.memory_path = self.root/'cam.json'

    def blocked(self):
        service = NavigationService(self.root/'first', agent_type=lambda **kw:
            ArachnidAgent(memory=CamMemory(self.memory_path), **kw))
        self.addCleanup(service.close)
        world = CameraWorld(profile='cold', session='first')
        for _ in range(100):
            d = json.loads(service.process(encoded(world.capture())))
            agent = service.sessions['first'].agent
            if d['action'] == 'move' and agent._effect(d['command']) is not None:
                dx,dz = world.effects[d['command']]
                wall = world.obs['x']+dx,world.obs['z']+dz
                world.walls.add(wall)
                world.step(d)
                obs = world.capture(); service.process(encoded(obs))
                return agent, world, d, obs, wall
            self.assertIn(d['action'], ('move','scan')); world.step(d)
        self.fail('missing motor calibration')

    def restart(self, first, obs):
        service = NavigationService(self.root/'second', agent_type=lambda **kw:
            ArachnidAgent(memory=CamMemory(self.memory_path), **dict(kw, core=copy.deepcopy(first.core))))
        self.addCleanup(service.close)
        obs = dict(obs, session='second', seq=0, outcome=empty_outcome())
        service.process(encoded(obs))
        return service.sessions['second'].agent, obs

    def test_second_encounter_veto_safe_context_and_extinction_survive_restart(self):
        first,world,d,obs,wall = self.blocked()
        saved = {s:canonical(row) for s,row in first.episodes.items()}
        second,obs2 = self.restart(first,obs)
        self.assertEqual(second._decision(obs2,'move','controlled repeat',command=d['command'])['action'],'wait')
        safe = dict(obs2, x=obs2['x']+2)
        self.assertEqual(second._decision(safe,'move','safe similar context',command=d['command'])['action'],'move')
        world.walls.remove(wall)
        changed = dict(obs2,rays=[dict(sx=obs['x']*2000,sz=obs['z']*2000,ex=wall[0]*2000,ez=wall[1]*2000,hit=False)])
        second._remember(changed)
        self.assertEqual(second._decision(changed,'move','obstacle removed',command=d['command'])['action'],'move')
        third = CamMemory(self.memory_path)
        self.assertEqual(third.cam.destroyed,0)
        self.assertFalse(third.active_blocks)
        self.assertEqual(saved,{s:canonical(row) for s,row in third.episodes.items()})
        self.assertEqual(len(third.payload()['slots']),4096)
        previous=second._decision(changed,'move','new physical attempt',command=d['command'])
        second.last_decision,second.last_observation=previous,changed
        cancelled=dict(changed,seq=changed['seq']+1,
                       outcome=receipt(previous,_point(changed),_point(changed),'cancelled'))
        second._ingest(cancelled)
        self.assertIsNone(second._blocked_memory(cancelled,d['command']),
                          'cancelled motion must not resurrect an extinct threat')

    def test_tampered_checkpoint_rejected_even_when_envelope_rehashed(self):
        first,*_ = self.blocked()
        data = json.loads(self.memory_path.read_bytes())
        slot = next(i for i,row in enumerate(data['slots']) if row)
        data['slots'][slot]['extract'] += 'forged'
        body={k:v for k,v in data.items() if k!='sha256'};data['sha256']=digest(body)
        self.memory_path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError,'episode'):
            CamMemory(self.memory_path)

    def test_different_world_does_not_restore_veto(self):
        first,world,d,obs,wall = self.blocked()
        second,obs2 = self.restart(first,dict(obs,world='different-world'))
        self.assertIsNone(second._blocked_memory(obs2,d['command']))

    def test_new_motor_epoch_in_same_world_does_not_restore_veto(self):
        first,world,d,obs,wall = self.blocked()
        # Change the core that will actually be checkpointed, not a detached view.
        first.core._epoch += 1
        second,obs2 = self.restart(first,obs)
        self.assertNotEqual(first.model['epoch'],second.model['epoch'])
        self.assertIsNone(second._blocked_memory(obs2,d['command']))
        self.assertEqual(second._decision(obs2,'move','changed dynamics',command=d['command'])['action'],'move')


if __name__=='__main__': unittest.main()
