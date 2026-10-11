"""Verify the bounded archived evidence without Unity or a new campaign."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT))
from Bridge.echo_source import load_core
from Tools.audit_physical_navigation import audit


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, default=PROJECT / 'Evidence/2026-10-11')
    parser.add_argument('--output', type=Path, default=PROJECT / 'Logs/evidence-check.json')
    parser.add_argument('--tamper-tests', action='store_true', help='Also run the existing 23 isolated tamper tests')
    args = parser.parse_args(argv)
    archive, output = args.archive.resolve(), args.output.resolve()
    if output.is_relative_to(archive):
        parser.error('--output must be outside the unchanged evidence archive')
    result = dict(schema='echounity.evidence-check.v1', valid=False,
                  scope='Archive hashes, journal chains and acquisition/development replay; no Unity or campaign')
    try:
        manifest = json.loads((archive / 'MANIFEST.json').read_text())
        for entry in manifest['files']:
            path = (archive / entry['path']).resolve()
            if not path.is_relative_to(archive):
                raise ValueError('manifest path outside archive')
            raw = path.read_bytes()
            if len(raw) != entry['bytes'] or hashlib.sha256(raw).hexdigest() != entry['sha256']:
                raise ValueError('archived evidence changed: ' + entry['path'])
        result['filesVerified'] = len(manifest['files'])
        result['bytesVerified'] = sum(entry['bytes'] for entry in manifest['files'])
        load_core()
        from echo5.trace import verify_journal
        result['journals'] = {entry['path']: verify_journal(archive / entry['path'])
                              for entry in manifest['files'] if entry['path'].endswith('.jsonl')}
        root = archive / manifest['physicalRoot']
        receipt = archive / manifest['physicalReceipt']
        physical = audit(root, receipt, manifest['recordedPhysicalRoot'])
        result['physical'] = physical
        if not physical['valid']:
            raise ValueError('physical evidence rejected: ' + json.dumps(physical['errors']))
        if args.tamper_tests:
            (PROJECT / 'Logs').mkdir(exist_ok=True)
            command = [sys.executable, '-B', str(PROJECT / 'Tools/test_audit_physical_navigation.py'),
                       '--root', str(root), '--integration', str(receipt),
                       '--recorded-root', manifest['recordedPhysicalRoot']]
            completed = subprocess.run(command, cwd=PROJECT, timeout=90, check=False)
            result['tamperTestsExitCode'] = completed.returncode
            if completed.returncode:
                raise ValueError('physical evidence tamper tests failed')
        result['valid'] = True
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, ImportError, subprocess.SubprocessError) as error:
        result['error'] = str(error)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({key: value for key, value in result.items() if key not in ('journals', 'physical')}))
    return 0 if result['valid'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
