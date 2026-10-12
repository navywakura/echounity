import json
from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from station_agent import BridgeService
from station_basal import BasalStationAgent
from test_station_agent import observation,obj,encoded,PublicWorld


class StationBasalTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.service=BridgeService(artifact_root=self.tmp.name,agent_type=BasalStationAgent)
        self.addCleanup(self.service.close)

    def test_actual_low_battery_and_closed_door_are_recorded(self):
        obs=observation(battery=12,objects=[obj('door','door',3,2,locked=True),obj('home','charger',2,2)])
        d=json.loads(self.service.process(encoded(obs)))
        self.assertEqual(d['basal']['E'][4],80)
        self.assertEqual(d['basal']['E'][2],0);self.assertEqual(d['basal']['E'][5],0)
        agent=self.service.sessions[obs['session']].agent
        slot=agent.doors['door'];row=agent.memory.episodes[slot]
        self.assertEqual(row['receipt']['door'],obs['objects'][0])
        veto=agent._decision(obs,'interact','probe','same unavailable door',item=obs['objects'][0])
        self.assertEqual(veto['action'],'wait')
        new=dict(obs,seq=1,inventory=['key']);agent._consequence(new)
        self.assertNotIn('door',agent.doors)
        self.assertEqual(agent.memory.episodes[slot],row)
        self.assertEqual(agent.memory.cam.destroyed,0)

    def test_three_complete_public_missions_with_wsp_receipts(self):
        for mission in ('irrigation','delivery','detour'):
            world=PublicWorld(mission)
            for _ in range(300):
                obs=world.observation();d=json.loads(self.service.process(encoded(obs)))
                if d['action'] in ('done','wait'):break
                world.step(d)
            self.assertEqual(d['action'],'done',d['reason'])
            self.assertEqual(d['basal']['E'][0],40)
            result=json.loads(self.service.finish(encoded(obs)))
            self.assertTrue(result['completed']);self.assertFalse(result.get('pending',False))
            self.assertGreater(result['basal']['camUsed'],0)


if __name__=='__main__':unittest.main()
