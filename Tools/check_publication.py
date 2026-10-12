"""Read-only source/archive verification; never opens Unity or a new exam."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

from audit_arachnid_state import audit

ROOT = Path(__file__).resolve().parents[1]


def verify_files(root, entries):
    root = root.resolve()
    seen = set()
    total = 0
    for entry in entries:
        relative = Path(entry['path'])
        if relative.is_absolute() or '..' in relative.parts:
            raise ValueError('non-relative manifest path')
        path = (root / relative).resolve()
        if not path.is_relative_to(root) or path in seen:
            raise ValueError('duplicate or escaping manifest path')
        seen.add(path)
        data = path.read_bytes()
        if len(data) != entry['bytes'] or hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise ValueError('hash/length mismatch: ' + str(relative))
        total += len(data)
    return dict(files=len(seen), bytes=total)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'Logs/publication-verification.json')
    args = parser.parse_args()
    output = args.output.resolve()
    # Results must never replace a source or the archived evidence being checked.
    if not output.is_relative_to(ROOT / 'Logs'):
        parser.error('--output must be inside the ignored Logs directory')
    result = dict(schema='echounity.publication-verification.v1', valid=False,
                  scope='Published file hashes and 34 original closure journals; no mission or new confirmation')
    try:
        manifest = json.loads((ROOT / 'SOURCE-MANIFEST.json').read_bytes())
        result['sources'] = verify_files(ROOT, manifest['files'])
        archive = ROOT / 'Evidence/2026-10-12'
        saved = json.loads((archive / 'MANIFEST.json').read_bytes())
        result['archive'] = verify_files(archive, saved['files'])
        closure = (archive / saved['latestClosure']).resolve()
        if not closure.is_relative_to(archive):
            raise ValueError('closure path outside archive')
        status = json.loads((closure / 'status.json').read_bytes())
        original = json.loads((closure / 'audit-final.json').read_bytes())
        if not status.get('valid') or status.get('development_only') or not original.get('valid'):
            raise ValueError('reference is not a completed closure')
        journals = []
        for row in original['journals']:
            relative = Path(row['journal']).relative_to(saved['recordedRoot'])
            journal = (archive / relative).resolve()
            if not journal.is_relative_to(closure):
                raise ValueError('reference journal outside closure')
            checked = audit(journal, recorded_root=saved['recordedRoot'], archive_root=archive)
            if not checked['valid'] or checked['sha256'] != row['sha256']:
                raise ValueError('original closure journal rejected: ' + str(relative))
            journals.append(dict(path=str(relative), sha256=checked['sha256'], valid=True))
        if len(journals) != 34 or len({v['path'] for v in journals}) != 34:
            raise ValueError('expected 34 distinct closure journals')
        result['closure_journals'] = journals
        result['valid'] = True
    except (OSError, ValueError, KeyError, TypeError, AssertionError, RuntimeError) as exc:
        result['error'] = str(exc)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: v for k, v in result.items() if k != 'closure_journals'}, indent=2))
    return 0 if result['valid'] else 1


if __name__ == '__main__':
    sys.exit(main())
