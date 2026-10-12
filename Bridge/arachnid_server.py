"""Arachnid presentation server. Uses the existing navigation service and trace writer.

GET /diagnostic?session=<id> exposes the measured, non-policy Layer256 observer.
It cannot choose actions or mutate the actor. E[6]/CAM are updated by the actor
from physical receipts, independently of whether the HUD is open.
"""
import argparse
from collections import OrderedDict, deque
import json
from pathlib import Path
import time
from urllib.parse import urlsplit, parse_qs

from server import Handler, make_server
from navigation import NavigationService
from arachnid_state import ArachnidAgent
from cam_persistence import CamMemory
from station_basal import BasalStationAgent
from station_agent import BridgeService, ProtocolError
from trace_inspector import inspect_trace
from echo5.neural_observer import NeuralObserver


class Diagnostic:
    def __init__(self):
        self.observer = NeuralObserver()
        self.cache = OrderedDict()

    def snapshot(self, token, session):
        # NavigationService releases the actor after acknowledging completion.
        # Keep displaying the last measured frame; never recreate a closed actor.
        if session.closed or session.agent is None:
            for (cached_token, _), result in reversed(self.cache.items()):
                if cached_token == token:
                    # Final receipt may contain a later movement than the observer frame.
                    basal = json.loads(session.finish_response).get('basal') if session.finish_response else None
                    return dict(result, basal=basal) if basal is not None else result
            return None
        obs = session.agent.last_observation
        if obs is None:
            return None
        key = (token, obs['seq'])
        if key in self.cache:
            self.cache.move_to_end(key)
            return self.cache[key]
        started = time.perf_counter()
        result = self.observer.observe(obs)
        result.update(session=token, sequence=obs['seq'],
                      homeostasis_available=False, cam_available=False,
                      observer_seconds=time.perf_counter()-started)
        if hasattr(session.agent, 'basal_snapshot'):
            result.update(homeostasis_available=True, cam_available=True,
                          basal=session.agent.basal_snapshot())
        # The existing thought.jsonl is the only journal. The view never enters policy.
        if not session.closed:
            session.recorder.write(kind='arachnid_observation_diagnostic', diagnostic=result)
        self.cache[key] = result
        while len(self.cache) > 16:
            self.cache.popitem(last=False)
        return result


class ArachnidHandler(Handler):
    def do_POST(self):
        started = time.perf_counter_ns()
        super().do_POST()
        if self.path in ('/navigate','/decide'):
            if not hasattr(self.server, 'decision_timings'):
                self.server.decision_timings = deque(maxlen=8192)
            self.server.decision_timings.append((time.perf_counter_ns()-started)/1_000_000)

    def do_GET(self):
        parsed = urlsplit(self.path)
        if parsed.path == '/metrics':
            if not self._local():return
            return self._send(200, dict(drives_policy=False,scope='HTTP server request including actor, serialization and socket write',
                                        decision_latency_ms=list(getattr(self.server,'decision_timings',()))))
        if parsed.path in ('/', '/trace.js', '/trace.css', '/sessions', '/trace'):
            if not self._local():
                return
            if parsed.path in ('/', '/trace.js', '/trace.css'):
                name = 'trace.html' if parsed.path == '/' else parsed.path[1:]
                data = Path(__file__).with_name(name).read_bytes()
                self.send_response(200)
                self.send_header('Content-Type', {'trace.html':'text/html; charset=utf-8',
                    'trace.js':'text/javascript; charset=utf-8','trace.css':'text/css; charset=utf-8'}[name])
                self.send_header('Content-Length', str(len(data)))
                self.send_header('Cache-Control', 'no-store')
                self.send_header('Content-Security-Policy', "default-src 'self'; frame-ancestors 'none'; object-src 'none'")
                self.end_headers(); self.wfile.write(data)
                return
            sessions = dict(self.server.service.sessions)
            sessions.update(self.server.navigation_service.sessions)
            if parsed.path == '/sessions':
                return self._send(200, [dict(session=k, seq=s.seq, closed=s.closed) for k,s in sessions.items()])
            params = parse_qs(parsed.query)
            token = params.get('session', [''])[0]
            if set(params) != {'session'} or len(params['session']) != 1 or token not in sessions:
                return self._send(404, {'error':'known_session_required'})
            s = sessions[token]
            try:
                journal = json.loads(s.response)['journal']
                return self._send(200, inspect_trace(journal))
            except (ValueError, KeyError, OSError):
                return self._send(409, {'error':'trace_not_available'})
        if parsed.path != '/diagnostic':
            return super().do_GET()
        if not self._local():
            return
        params = parse_qs(parsed.query)
        tokens = params.get('session', [])
        if len(tokens) != 1 or set(params) != {'session'}:
            return self._send(400, {'error':'one_session_required'})
        token = tokens[0]
        service = self.server.navigation_service
        with service.lock:
            session = service.sessions.get(token)
            if session is None:
                return self._send(404, {'error':'session_not_observed'})
            snapshot = self.server.diagnostic.snapshot(token, session)
        self._send(200, snapshot or {'error':'observation_pending'})


class PersistentNavigationService(NavigationService):
    def __init__(self, root, persistence=True):
        self.persistence = persistence
        self.cam_path = Path(root) / 'cam.json'
        super().__init__(root, agent_type=self._actor)

    def _actor(self, **kwargs):
        return ArachnidAgent(memory=CamMemory(self.cam_path if self.persistence else None), **kwargs)

    def _new_session(self, obs):
        if self.persistence and any(not s.closed for s in self.sessions.values()):
            raise ProtocolError(409, 'persistent_memory_requires_previous_session_finish')
        return super()._new_session(obs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8771)
    parser.add_argument('--artifacts', type=Path, required=True)
    parser.add_argument('--no-persistence', action='store_true', help='Fresh CAM per session for controlled comparisons')
    args = parser.parse_args()
    server = make_server(port=args.port,
        service=BridgeService(artifact_root=args.artifacts/'station', agent_type=BasalStationAgent),
        navigation_service=PersistentNavigationService(args.artifacts, not args.no_persistence))
    server.RequestHandlerClass = ArachnidHandler
    server.diagnostic = Diagnostic()
    print(json.dumps({'status':'ready','port':server.server_address[1],
                      'artifacts':str(args.artifacts),'observer_drives_policy':False}), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close();server.navigation_service.close();server.service.close()


if __name__ == '__main__':
    main()
