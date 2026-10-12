"""Evidence and causal checks for the optional E[6]/CAM actor adapter."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from arachnid_state import ArachnidAgent, CAM_SLOTS, unpack
from arachnid_server import Diagnostic
from navigation import NavigationService, NavigationAgent, ProtocolError, _point
from test_navigation import CameraWorld, encoded, observation, receipt


class NoUrgency(ArachnidAgent):
    urgency_control = False


class NoCamControl(ArachnidAgent):
    memory_control = False


class BasalTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='echo-arachnid-state-')
        self.addCleanup(self.tmp.cleanup)

    def service(self, actor=ArachnidAgent, label='main'):
        service = NavigationService(Path(self.tmp.name)/label, agent_type=actor)
        self.addCleanup(service.close)
        return service

    def blocked(self, actor=ArachnidAgent, label='main', learned=False):
        service = self.service(actor, label)
        world = CameraWorld(profile='cold', session=label)
        for _ in range(60):
            obs = world.capture()
            d = json.loads(service.process(encoded(obs)))
            agent = service.sessions[label].agent
            if d['action'] == 'move' and (not learned or agent._effect(d['command']) is not None):
                dx, dz = world.effects[d['command']]
                wall = world.obs['x']+dx, world.obs['z']+dz
                world.walls.add(wall)  # physical fixture changes AFTER the proposal
                world.step(d)
                after = world.capture()
                b = json.loads(service.process(encoded(after)))
                return service, world, agent, d, b, after, wall
            self.assertIn(d['action'], ('move', 'scan'))
            world.step(d)
        self.fail('no eligible motion in fixture')

    def test_initial_state_is_not_an_episode(self):
        service = self.service()
        obs = observation()
        d = json.loads(service.process(encoded(obs)))
        s = d['basal']
        self.assertEqual(s['camUsed'], 0)
        self.assertEqual(s['camCapacity'], 4096)
        self.assertEqual(len(bytes.fromhex(s['wsp'])), 16)
        self.assertEqual(list(unpack(bytes.fromhex(s['wsp']))['E'].values()), s['E'])
        self.assertTrue(all(type(n) is int and -100 <= n <= 100 for n in s['E']))
        self.assertEqual([s['E'][2], s['E'][5]], [0, 0])

    def test_block_receipt_raises_urgency_and_uses_real_cam_to_inspect(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        self.assertEqual(b['action'], 'scan')
        self.assertEqual(b['basal']['E'][4], 80)
        self.assertEqual(b['basal']['urgencyScans'], 1)
        self.assertGreater(b['basal']['memoryReads'], 0)
        slot = agent.cam.slot[b['basal']['lastRead']]
        self.assertEqual(slot.has_fact, 1)
        recorded = json.loads(slot.extract)
        self.assertEqual(recorded['outcome'], obs['outcome'])
        self.assertNotIn('target', recorded)  # a failed motor attempt is not a measured wall
        self.assertIn('arachnid_cam_write', agent.budget.snapshot()['counts'])

    def test_removing_either_control_removes_its_extra_inspection(self):
        *_, active, obs, wall = self.blocked(ArachnidAgent, 'active', learned=True)
        self.assertEqual(active['action'], 'scan')
        for actor in (NoUrgency, NoCamControl):
            *_, b, obs, wall = self.blocked(actor, actor.__name__, learned=True)
            self.assertEqual(b['basal']['urgencyScans'], 0)
            self.assertEqual(b['basal']['E'][4], 80)  # telemetry alone is not causality
            self.assertEqual(b['action'], 'move')  # same physical failure, different next action

    def test_byte_retry_has_no_second_episode_or_state_update(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        before = (agent.basal_snapshot(), agent.budget.snapshot(), Path(agent.journal_path).read_bytes())
        raw = service.process(encoded(obs))
        self.assertEqual(json.loads(raw), b)
        self.assertEqual(before, (agent.basal_snapshot(), agent.budget.snapshot(), Path(agent.journal_path).read_bytes()))

    def test_cancelled_or_mismatched_receipt_does_not_create_fact(self):
        for mismatch in (False, True):
            service = self.service(label=str(mismatch))
            obs = observation(session=str(mismatch))
            d = json.loads(service.process(encoded(obs)))
            o = receipt(d, (3,3), (3,3), 'cancelled')
            if mismatch:
                o['seq'] += 1
            after = dict(obs, seq=1, outcome=o)
            if mismatch:
                with self.assertRaises(ProtocolError):
                    service.process(encoded(after))
                self.assertEqual(service.sessions[str(mismatch)].agent.cam.used, 0)
            else:
                b = json.loads(service.process(encoded(after)))
                self.assertEqual(b['basal']['camUsed'], 0)

    def test_active_cam_veto_and_refutation_preserve_historical_fact(self):
        service, world, agent, d, b, obs, wall = self.blocked(learned=True)
        count = agent.cam.used
        self.assertEqual(agent._decision(obs, 'move', 'unit guard probe', command=d['command'])['action'], 'wait')
        changed = copy.deepcopy(obs)
        changed['rays'] = [dict(sx=obs['x']*2000, sz=obs['z']*2000,
                               ex=wall[0]*2000, ez=wall[1]*2000, hit=False)]
        agent._remember(changed)  # new depth measurement clears the inference, not the receipt
        self.assertNotIn((_point(obs), d['command']), agent.blocked_edges)
        self.assertEqual(agent._decision(changed, 'move', 'unit guard probe', command=d['command'])['action'], 'move')
        self.assertEqual(agent.cam.used, count)
        self.assertEqual(agent.cam.destroyed, 0)

    def test_wrong_context_or_corrupt_extract_cannot_veto(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        other = dict(obs, world='another-world')
        self.assertIsNone(agent._blocked_memory(other, d['command']))
        agent.model['epoch'] += 1
        self.assertIsNone(agent._blocked_memory(obs, d['command']))
        agent.model['epoch'] -= 1
        agent.cam.slot[agent.last_written].extract += 'tampered'
        self.assertIsNone(agent._blocked_memory(obs, d['command']))

    def test_successive_scans_record_receipts_and_decay_urgency_to_floor(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        # Targeted ingestion of matched scan receipts, independent of route selection.
        for n in range(30):
            previous = agent._decision(obs, 'scan', 'decay fixture', yaw=(obs['yaw']+90)%360)
            agent.last_decision, agent.last_observation = previous, obs
            obs = dict(obs, seq=obs['seq']+1, yaw=previous['yaw'],
                       outcome=receipt(previous, _point(obs), _point(obs), 'scanned'))
            agent._ingest(obs)
        self.assertEqual(agent.E[4], 10)
        self.assertEqual(agent.cam.destroyed, 0)
        self.assertGreater(agent.cam.used, 30)

    def test_full_cam_never_evicts_or_invents_an_episode(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        existing = copy.deepcopy(agent.episodes)
        agent.cam.used = CAM_SLOTS  # capacity path; do not allocate a second memory
        self.assertEqual(agent._store(obs), -1)
        self.assertEqual(agent.episodes, existing)
        self.assertEqual(agent.cam.destroyed, 0)
        self.assertEqual(agent.dropped, 1)

    def test_diagnostic_does_not_change_cam_e6_or_actions_and_keeps_final_state(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        diagnostic = Diagnostic()
        before = (agent.basal_snapshot(), agent.budget.snapshot())
        session = service.sessions[world.obs['session']]
        s = diagnostic.snapshot(world.obs['session'], session)
        self.assertTrue(s['homeostasis_available'] and s['cam_available'])
        self.assertEqual(before, (agent.basal_snapshot(), agent.budget.snapshot()))
        world.step(b)
        final = json.loads(service.finish(encoded(world.capture())))
        self.assertEqual(diagnostic.snapshot(world.obs['session'], session)['basal'], final['basal'])
        self.assertEqual(service.finish(encoded(world.capture())), session.finish_response)

    def test_adapter_has_no_success_regression_in_four_paired_worlds(self):
        results = []
        for i, walls in enumerate(((), ((4,4),), ((2,3),(4,3)), ((3,5),(4,5),(5,5)))):
            pair = []
            for actor in (NavigationAgent, ArachnidAgent):
                label = f'{i}-{actor.__name__}'
                service = self.service(actor, label)
                world = CameraWorld(profile='cold', session=label, walls=walls)
                for _ in range(160):
                    obs = world.capture()
                    d = json.loads(service.process(encoded(obs)))
                    if d['action'] not in ('move','scan'):
                        break
                    world.step(d)
                self.assertIn(d['action'], ('done','wait'), (label,d['reason']))
                final = json.loads(service.finish(encoded(obs)))
                self.assertLess(final['budgetUsed'], 2_000_000)
                pair.append(dict(actor=actor.__name__,completed=final['completed'],
                                 budgetUsed=final['budgetUsed'], metrics=final['metrics']))
                if actor is ArachnidAgent:
                    self.assertGreater(final['basal']['camUsed'], 0)
                    self.assertEqual(final['basal']['camDestroyed'], 0)
            self.assertGreaterEqual(pair[1]['completed'], pair[0]['completed'], (i,pair))
            results.append(dict(world=i,arms=pair))
        print('ARACHNID_PAIRED_RESULTS='+json.dumps(results, sort_keys=True))

    def test_cam_write_budget_exhaustion_stops_before_another_action(self):
        service = self.service()
        world = CameraWorld(profile='cold', session='budget')
        d = json.loads(service.process(encoded(world.capture())))
        agent = service.sessions['budget'].agent
        world.step(d)
        agent.budget.limit = agent.budget.used + 2000
        b = json.loads(service.process(encoded(world.capture())))
        self.assertEqual(b['action'], 'wait')
        self.assertEqual(b['basal']['camUsed'], 0)
        self.assertFalse(agent.valid)
        self.assertGreaterEqual(b['budgetRemaining'], 0)

    def test_ephemeral_cam_is_not_shared_between_sessions(self):
        service, world, agent, d, b, obs, wall = self.blocked()
        other = json.loads(service.process(encoded(observation(session='new-session'))))
        self.assertEqual(other['basal']['camUsed'], 0)
        self.assertEqual(other['basal']['E'][4], 10)


if __name__ == '__main__':
    unittest.main()
