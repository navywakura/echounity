import copy
import http.client
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from arachnid_server import ArachnidHandler, Diagnostic
from arachnid_state import ArachnidAgent
from navigation import NavigationService
from server import make_server
from test_navigation import observation,encoded


class InspectorTests(unittest.TestCase):
    def test_http_inspector_is_passive_and_uses_actual_journal(self):
        with tempfile.TemporaryDirectory() as tmp:
            service=NavigationService(Path(tmp),agent_type=ArachnidAgent)
            server=make_server(port=0,navigation_service=service);server.RequestHandlerClass=ArachnidHandler
            server.diagnostic=Diagnostic();thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
            try:
                obs=observation();reply=service.process(encoded(obs));actor=service.sessions[obs['session']].agent
                before=(actor.basal_snapshot(),actor.budget.snapshot(),Path(actor.journal_path).read_bytes())
                port=server.server_address[1]
                for path in ('/','/trace.js','/trace.css','/sessions','/trace?session='+obs['session']):
                    connection=http.client.HTTPConnection('127.0.0.1',port,timeout=5)
                    connection.request('GET',path);response=connection.getresponse();raw=response.read()
                    self.assertEqual(response.status,200,(path,raw))
                    if path.startswith('/trace?'):
                        data=json.loads(raw);self.assertFalse(data['drives_policy'])
                        self.assertEqual(data['steps'][0]['observation'],obs)
                        self.assertEqual(data['steps'][0]['decision'],json.loads(reply))
                    connection.close()
                self.assertEqual(before,(actor.basal_snapshot(),actor.budget.snapshot(),Path(actor.journal_path).read_bytes()))
            finally:server.shutdown();server.server_close();thread.join();service.close();server.service.close()


if __name__=='__main__':unittest.main()
