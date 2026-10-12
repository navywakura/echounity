"""Regression from the user's public cold-start trajectory, without a hidden map."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from navigation import NavigationAgent, Budget, Recorder, HypothesisAgent, _point


class RepositionTests(unittest.TestCase):
    def history(self):
        fixture=json.loads((Path(__file__).parent/'fixtures/arachnid-cold-stall.json').read_text())
        tmp=tempfile.TemporaryDirectory();self.addCleanup(tmp.cleanup)
        recorder=Recorder(Path(tmp.name)/'thought.jsonl');self.addCleanup(recorder.close)
        agent=NavigationAgent(core=HypothesisAgent(),budget=Budget(2_000_000),recorder=recorder,
                              journal_path=Path(tmp.name)/'thought.jsonl')
        # Ingest only recorded outcomes; historical proposals are not attributed to the fix.
        for obs,decision in zip(fixture['observations'],fixture['decisions']):
            agent._ingest(obs)
            agent.last_observation=obs
            agent.last_decision=decision
            if decision['action']=='move':agent.attempts[(_point(obs),decision['command'])]+=1
        self.assertEqual(agent.model['support'],[2,1,2,1])
        return agent,fixture['observations'][-1]

    def test_confirmed_edge_can_reach_a_new_calibration_sample(self):
        agent,obs=self.history();before=agent.core.checkpoint()
        decision=agent._choose(obs)
        self.assertEqual(decision['action'],'move',decision['reason'])
        self.assertEqual(agent.core.checkpoint(),before)  # choosing creates no synthetic support
        origin=_point(obs);target=agent._target(origin,decision['command'])
        receipts=[r['outcome'] for r in agent.calibrations]
        self.assertTrue(any(r['fromX']==origin[0] and r['fromZ']==origin[1]
                            and r['command']==decision['command'] and (r['x'],r['z'])==target
                            for r in receipts))

    def test_reposition_cannot_borrow_receipts_from_another_session(self):
        agent,obs=self.history()
        self.assertIsNone(agent._reposition_for_probe(dict(obs,session='another-session')))

    def test_reposition_respects_blocked_edges_and_never_invents_them(self):
        agent,obs=self.history()
        for sample in agent.calibrations:
            r=sample['outcome'];origin=(r['fromX'],r['fromZ']);command=r['command']
            agent.blocked_edges[(origin,command)]=dict(target=[r['x'],r['z']],infer_cell=False)
        self.assertIsNone(agent._reposition_for_probe(obs))
        agent.blocked_edges.clear();agent.calibrations.clear()
        self.assertIsNone(agent._reposition_for_probe(obs))

    def test_no_memory_never_replays_history(self):
        agent,obs=self.history()
        self.assertIsNone(agent._reposition_for_probe(dict(obs,profile='no_memory')))


if __name__=='__main__':unittest.main()
