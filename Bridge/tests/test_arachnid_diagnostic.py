"""Presentation observations must not alter the actor or consume its budget."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from navigation import NavigationService
from arachnid_server import Diagnostic
from test_navigation import observation, encoded, CameraWorld


class DiagnosticTests(unittest.TestCase):
    def test_snapshot_uses_real_layer_and_keeps_policy_unchanged(self):
        with tempfile.TemporaryDirectory() as tmp:
            service=NavigationService(Path(tmp))
            try:
                obs=observation();reply=service.process(encoded(obs));session=service.sessions[obs['session']]
                before=(session.agent.core.checkpoint(),session.agent.budget.snapshot(),copy.deepcopy(session.agent.metrics),session.response)
                observer=Diagnostic();data=observer.snapshot(obs['session'],session)
                after=(session.agent.core.checkpoint(),session.agent.budget.snapshot(),copy.deepcopy(session.agent.metrics),session.response)
                self.assertEqual(before,after)
                self.assertFalse(data['drives_policy']);self.assertEqual(data['neurons'],256)
                self.assertEqual(len(data['frames']),16);self.assertGreater(data['spikes_total'],0)
                self.assertFalse(data['homeostasis_available']);self.assertFalse(data['cam_available'])
                size=Path(session.agent.journal_path).stat().st_size
                self.assertIs(data,observer.snapshot(obs['session'],session))
                self.assertEqual(size,Path(session.agent.journal_path).stat().st_size)
                self.assertEqual(reply,service.process(encoded(obs)))
            finally: service.close()

    def test_diagnostic_does_not_change_subsequent_actions(self):
        with tempfile.TemporaryDirectory() as tmp:
            left=NavigationService(Path(tmp)/'left');right=NavigationService(Path(tmp)/'right')
            world=CameraWorld(profile='cold',session='same');observer=Diagnostic()
            try:
                for _ in range(8):
                    obs=world.capture();a=json.loads(left.process(encoded(obs)));b=json.loads(right.process(encoded(obs)))
                    a.pop('journal');b.pop('journal');self.assertEqual(a,b)
                    observer.snapshot('same',left.sessions['same'])
                    if a['action'] not in ('scan','move'):break
                    world.step(a)
            finally:left.close();right.close()

    def test_closed_session_reuses_last_frame_without_reopening_journal(self):
        with tempfile.TemporaryDirectory() as tmp:
            service=NavigationService(Path(tmp));observer=Diagnostic()
            try:
                obs=observation();service.process(encoded(obs));session=service.sessions[obs['session']]
                path=Path(session.agent.journal_path)
                measured=observer.snapshot(obs['session'],session)
                service.finish(encoded(obs));before=path.read_bytes()
                self.assertIsNone(session.agent)
                self.assertIs(observer.snapshot(obs['session'],session),measured)
                self.assertIsNone(Diagnostic().snapshot(obs['session'],session))
                self.assertEqual(path.read_bytes(),before)
            finally:service.close()


if __name__=='__main__':unittest.main()
