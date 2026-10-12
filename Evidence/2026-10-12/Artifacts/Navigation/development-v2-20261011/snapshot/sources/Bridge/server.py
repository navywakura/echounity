"""Stdlib, loopback-only Unity bridge. Start with: python3 -B server.py"""

import argparse
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
from pathlib import Path
import socket

from station_agent import BridgeService, MAX_BODY, ProtocolError
from navigation import NavigationService, DEFAULT_ARTIFACTS as NAVIGATION_ARTIFACTS


class LoopbackServer(HTTPServer):
    allow_reuse_address = True
    request_queue_size = 8

    def get_request(self):
        connection, address = super().get_request()
        connection.settimeout(5)
        return connection, address

    def verify_request(self, request, client_address):
        return client_address[0] == "127.0.0.1"


class Handler(BaseHTTPRequestHandler):
    server_version = "EchoStation/1"
    sys_version = ""
    protocol_version = "HTTP/1.0"

    def log_message(self, format, *args):
        # Avoid writing request paths, session identifiers or payloads to access logs.
        pass

    def _send(self, status, payload):
        if type(payload) is not bytes:
            payload = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Connection", "close")
        self.end_headers()
        self.close_connection = True
        try:
            self.wfile.write(payload)
        except (BrokenPipeError, ConnectionResetError):
            pass  # Decision remains cached if Unity disconnected before receiving it.

    def _local(self):
        port = self.server.server_address[1]
        hosts = self.headers.get_all("Host", [])
        allowed = {f"127.0.0.1:{port}", f"localhost:{port}"}
        if port == 80:
            allowed.update(("127.0.0.1", "localhost"))
        if (self.client_address[0] != "127.0.0.1" or len(hosts) != 1 or hosts[0].lower() not in allowed
                or self.headers.get_all("Origin")
                or self.headers.get("Sec-Fetch-Site", "").lower() not in ("", "none", "same-origin")):
            self._send(403, {"error": "local_native_client_required"})
            return False
        return True

    def do_GET(self):
        if not self._local():
            return
        if self.path == "/health":
            self._send(200, {"schema": "echo.station.health.v1", "status": "ok"})
        else:
            self._send(404, {"error": "not_found"})

    def do_POST(self):
        if not self._local():
            return
        if self.path not in ("/decide", "/finish", "/navigate", "/navigate/finish"):
            self._send(404, {"error": "not_found"})
            return
        try:
            if self.headers.get_all("Transfer-Encoding"):
                raise ProtocolError(400, "transfer_encoding_forbidden")
            lengths = self.headers.get_all("Content-Length", [])
            if not lengths:
                raise ProtocolError(411, "content_length_required")
            if len(lengths) != 1 or not lengths[0].isascii() or not lengths[0].isdecimal():
                raise ProtocolError(400, "invalid_content_length")
            if len(lengths[0]) > 10 or int(lengths[0]) > MAX_BODY:
                raise ProtocolError(413, "body_too_large")
            length = int(lengths[0])
            types = self.headers.get_all("Content-Type", [])
            if len(types) != 1 or types[0].lower().replace(" ", "") not in (
                    "application/json", "application/json;charset=utf-8"):
                raise ProtocolError(415, "application_json_required")
            if self.headers.get_all("Content-Encoding"):
                raise ProtocolError(415, "content_encoding_forbidden")
            raw = self.rfile.read(length)
            if len(raw) != length:
                raise ProtocolError(400, "truncated_body")
            if self.path.startswith("/navigate"):
                service = self.server.navigation_service
                operation = service.process if self.path == "/navigate" else service.finish
            else:
                operation = self.server.service.process if self.path == "/decide" else self.server.service.finish
            response = operation(raw)
        except ProtocolError as exc:
            self._send(exc.status, {"error": exc.code})
        except (TimeoutError, socket.timeout):
            self._send(408, {"error": "request_timeout"})
        except Exception:
            self._send(500, {"error": "bridge_failure"})
        else:
            self._send(200, response)

    def _unsupported(self):
        if self._local():
            self._send(405, {"error": "method_not_allowed"})

    do_OPTIONS = do_PUT = do_DELETE = do_PATCH = do_HEAD = _unsupported


def make_server(*, port=8770, service=None, navigation_service=None):
    if type(port) is not int or not 0 <= port <= 65535:
        raise ValueError("invalid port")
    server = LoopbackServer(("127.0.0.1", port), Handler)
    server.service = service if service is not None else BridgeService()
    server.navigation_service = (navigation_service if navigation_service is not None
                                 else NavigationService(NAVIGATION_ARTIFACTS))
    return server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8770)
    parser.add_argument("--navigation-root", type=Path, default=NAVIGATION_ARTIFACTS,
                        help="navigation journals, per-world maps and model.json (separate physical/CPU runs)")
    args = parser.parse_args()
    server = make_server(port=args.port, navigation_service=NavigationService(args.navigation_root))
    print(f"ECHO Station bridge http://127.0.0.1:{server.server_address[1]}; public observations only", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        server.service.close()
        server.navigation_service.close()


if __name__ == "__main__":
    main()
