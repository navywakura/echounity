"""Portable Flight Lab entry point; ECHO is imported, never vendored."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import runpy
import sys
import unittest

LAB = Path(__file__).resolve().parents[1]
REPO = LAB.parent


def load_echo():
    source = Path(os.environ.get('ECHOAI_SOURCE', REPO.parent / 'echoai')).expanduser().resolve()
    compatibility = json.loads((LAB / 'ECHO-COMPATIBILITY.json').read_text())
    for name, expected in compatibility['sha256'].items():
        path = source / name
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise SystemExit('ECHOAI_SOURCE must point to a compatible official echoai checkout. '
                             'See FlightLab/ECHO-COMPATIBILITY.json; missing or changed: ' + name)
    if not (source / '__init__.py').is_file():
        raise SystemExit('ECHOAI_SOURCE is missing __init__.py')
    spec = importlib.util.spec_from_file_location('echoai', source / '__init__.py',
                                                submodule_search_locations=[str(source)])
    echo = importlib.util.module_from_spec(spec)
    sys.modules['echoai'] = echo
    spec.loader.exec_module(echo)
    sys.path.insert(0, str(LAB / 'Adapter'))
    os.environ['ECHOAI_SOURCE'] = str(source)
    return source


def tests():
    suite = unittest.defaultTestLoader.discover(str(LAB / 'Tests'))
    return 0 if unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful() else 1


def verify():
    from echo_flightlab.audit import audit
    from echo_flightlab.physics import audit as physical_audit
    manifest = json.loads((LAB / 'MANIFEST.json').read_text())
    for row in manifest['files']:
        path = REPO / row['path']
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != row['sha256']:
            raise SystemExit('Published file changed: ' + row['path'])
    if tests():
        return 1
    for name in ['memx', 'empty', 'cache', 'changed', 'sensor-loss']:
        directory = LAB / 'Recordings' / name
        got = audit(directory)
        if got != json.loads((directory / 'audit.json').read_text()):
            raise SystemExit('Decision replay differs: ' + name)
        if physical_audit(directory) != json.loads((directory / 'physical-audit.json').read_text()):
            raise SystemExit('Physical evidence differs: ' + name)
        print(name + ': identical decision replay and physical audit')
    print('PASS: published hashes, 31 CPU tests, five original decision replays and physical audits; no flight or API.')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['tests', 'verify', 'doctor', 'run', 'world',
                                           'cleanup', 'flight', 'audit', 'physics',
                                           'school', 'checks', 'record'])
    args, rest = parser.parse_known_args()
    load_echo()
    if args.command == 'tests':
        return tests()
    if args.command == 'verify':
        return verify()
    if args.command in ['doctor', 'run', 'world', 'cleanup']:
        module = 'launch'
        rest = [args.command, *rest]
    else:
        module = {'record': 'videos'}.get(args.command, args.command)
    sys.argv = [str(Path(__file__).resolve()), *rest]
    runpy.run_module('echo_flightlab.' + module, run_name='__main__')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
