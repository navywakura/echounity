"""Controlled physical second-encounter assay, not an autonomous route benchmark.

The fixed proposal (command 0) isolates the existing CAM gate. Its meaning is
learned from actual Unity movements, saved, and replay-validated after restart.
"""
import argparse
import json
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]


def serve(root,port,control):
    sys.path.insert(0,str(ROOT/'Bridge'))
    from navigation import NavigationService, _atomic
    from arachnid_state import ArachnidAgent
    from cam_persistence import CamMemory
    from station_agent import HypothesisAgent
    from server import make_server
    from arachnid_server import ArachnidHandler, Diagnostic

    class ControlledProposal(ArachnidAgent):
        memory_control=not control
        def __init__(self,**kw):
            checkpoint=root/'empirical-motor.json'
            if checkpoint.exists():
                kw['core']=HypothesisAgent.restore(json.loads(checkpoint.read_bytes()),budget=kw['budget'])
            super().__init__(memory=CamMemory(root/'cam.json'),**kw)
            self.recorder.write(kind='conditioning_protocol',command=0,control_no_cam=control,
                                source='fixed experimental proposal; motor meaning acquired from physical receipts')
        def _ingest(self,obs,**kw):
            result=super()._ingest(obs,**kw)
            _atomic(root/'empirical-motor.json',self.core.checkpoint())
            return result
        def _choose(self,obs):
            self.last_gate='OK / sin modificación'
            if self.stopped:return self._decision(obs,'wait',self.stopped)
            return self._decision(obs,'move','Controlled repeated proposal; no claim of autonomous route choice.',command=0)

    server=make_server(port=port,navigation_service=NavigationService(root,agent_type=ControlledProposal))
    server.RequestHandlerClass=ArachnidHandler;server.diagnostic=Diagnostic()
    print(json.dumps(dict(ready=True,port=port,control=control)),flush=True)
    try:server.serve_forever()
    except KeyboardInterrupt:pass
    finally:server.server_close();server.navigation_service.close();server.service.close()


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--serve',action='store_true');p.add_argument('--root',type=Path,required=True)
    p.add_argument('--port',type=int,default=8771);p.add_argument('--no-cam',action='store_true')
    a=p.parse_args()
    if not a.serve:raise SystemExit('Use Tools/check.py --integration to run the complete physical assay.')
    serve(a.root.resolve(),a.port,a.no_cam)


if __name__=='__main__':main()
