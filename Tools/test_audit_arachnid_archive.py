"""Archive audit regressions using temporary evidence only; no exams or actors.

Run: python3 -B Tools/test_audit_arachnid_archive.py
"""

from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import audit_arachnid_state as auditor


def canonical(value, ascii=False):
    return json.dumps(value, ensure_ascii=ascii, sort_keys=True,
                      separators=(',', ':'), allow_nan=False).encode()


def write_journal(path, rows):
    previous = '0' * 64
    lines = []
    for index, value in enumerate(rows):
        row = dict(value, index=index, prev=previous,
                   schema='echo45-arc-decisions-v1', agent='echo5')
        previous = hashlib.sha256(canonical(row)).hexdigest()
        lines.append(canonical(dict(row, hash=previous)))
    path.write_bytes(b'\n'.join(lines) + b'\n')


class ArchiveAuditTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='arachnid-archive-audit-')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.recorded = self.root / 'original/ECHO-Station'
        self.archive = self.root / 'published/Evidence/2026-10-12'
        self.relative_run = Path('Artifacts/Closure/fixture')
        run = self.recorded / self.relative_run
        run.mkdir(parents=True)
        self.original_journal = run / 'thought.jsonl'
        self.original_checkpoint = run / 'cam.json'
        self.original_source = self.recorded / 'Artifacts/acquisition.jsonl'
        self.journal = self.archive / self.relative_run / 'thought.jsonl'
        self.checkpoint = self.journal.with_name('cam.json')
        self.source = self.archive / 'Artifacts/acquisition.jsonl'
        self.receipt = dict(context=dict(world='fixture-é', origin=[0, 0]),
                            outcome=dict(status='blocked'))
        self.receipt_hash = hashlib.sha256(canonical(self.receipt, ascii=True)).hexdigest()
        self.source_rows = [dict(kind='arachnid_cam_write', slot=0, wsp='00' * 16,
                                 extract=self.receipt, extract_sha256=self.receipt_hash)]
        write_journal(self.original_source, self.source_rows)
        slot = dict(occupied=1, has_fact=1, wsp='00' * 16,
                    extract=canonical(self.receipt, ascii=True).decode(),
                    source=str(self.original_source))
        self.data = dict(slots=[slot] + [None] * 4095, destroyed=0, used=1,
                         episodes={'0': dict(receipt=self.receipt, sha256=self.receipt_hash)})
        checkpoint_hash = hashlib.sha256(canonical(self.data, ascii=True)).hexdigest()
        self.original_checkpoint.write_bytes(canonical(dict(self.data, sha256=checkpoint_hash)))
        self.rows = [
            dict(kind='arachnid_cam_restore', checkpoint=str(self.original_checkpoint),
                 sha256=checkpoint_hash, used=1),
            dict(kind='arachnid_cam_read', slot=0, context=self.receipt['context'],
                 extract_sha256=self.receipt_hash, verified=True),
            dict(kind='navigation_finish', result=dict(
                status='closed', pending=False, completed=True,
                basal=dict(camUsed=1, camDestroyed=0, E=[0, 20, 0, 0, 10, 0], memoryReads=1),
                budgetUsed=3, budgetRemaining=1_999_997, metrics={})),
        ]
        write_journal(self.original_journal, self.rows)
        shutil.copytree(self.recorded, self.archive)

    def audit_archive(self, path=None):
        return auditor.audit(path or self.journal, recorded_root=self.recorded,
                             archive_root=self.archive)

    def reseal_checkpoint(self):
        digest = hashlib.sha256(canonical(self.data, ascii=True)).hexdigest()
        self.checkpoint.write_bytes(canonical(dict(self.data, sha256=digest)))
        self.rows[0]['sha256'] = digest
        write_journal(self.journal, self.rows)

    @contextmanager
    def forbid_original_access(self):
        # Real archive I/O still runs; any original open/resolve is a regression.
        original_open, original_resolve = Path.open, Path.resolve

        def guarded(method):
            def call(path, *args, **kwargs):
                if path.is_relative_to(self.recorded):
                    raise AssertionError('attempted original access: ' + str(path))
                return method(path, *args, **kwargs)
            return call

        with patch.object(Path, 'open', guarded(original_open)), \
                patch.object(Path, 'resolve', guarded(original_resolve)):
            yield

    def cli(self, *args):
        return subprocess.run([sys.executable, '-B', auditor.__file__, str(self.journal), *args],
                              capture_output=True, text=True, timeout=10)

    def test_default_one_argument_api_still_restores(self):
        report = auditor.audit(self.original_journal)
        self.assertTrue(report['valid'])
        self.assertEqual((report['events'], report['cam_receipts'], report['verified_reads']), (3, 1, 1))

    def test_archive_preserves_bytes_hashes_and_audit_result_without_original_access(self):
        expected = auditor.audit(self.original_journal)
        before = {path: path.read_bytes() for path in (self.journal, self.checkpoint, self.source)}
        with self.forbid_original_access():
            report = self.audit_archive()
        self.assertEqual(report, dict(expected, journal=str(self.journal)))
        self.assertEqual(before, {path: path.read_bytes() for path in before})
        for path, raw in before.items():
            self.assertEqual(raw, (self.recorded / path.relative_to(self.archive)).read_bytes())

    def test_archive_works_after_original_tree_is_removed(self):
        shutil.rmtree(self.recorded)
        self.assertTrue(self.audit_archive()['valid'])

    def test_recorded_root_is_a_lexical_namespace_even_if_it_is_a_symlink(self):
        moved = self.root / 'moved-original'
        self.recorded.rename(moved)
        self.recorded.symlink_to(moved, target_is_directory=True)
        with self.forbid_original_access():
            self.assertTrue(self.audit_archive()['valid'])

    def test_cli_accepts_mapping_and_writes_only_requested_report(self):
        shutil.rmtree(self.recorded)
        output = self.root / 'report.json'
        process = self.cli('--recorded-root', str(self.recorded), '--archive-root',
                           str(self.archive), '--output', str(output))
        self.assertEqual(process.returncode, 0, process.stderr)
        report = json.loads(process.stdout)
        self.assertTrue(report['valid'])
        self.assertEqual(report['sha256'], hashlib.sha256(self.journal.read_bytes()).hexdigest())
        self.assertEqual(report, json.loads(output.read_bytes()))

    def test_mapping_requires_both_roots_in_api_and_cli(self):
        for keyword, flag, value in (('recorded_root', '--recorded-root', self.recorded),
                                     ('archive_root', '--archive-root', self.archive)):
            with self.subTest(keyword=keyword):
                with self.assertRaisesRegex(ValueError, 'together'):
                    auditor.audit(self.root / 'missing.jsonl', **{keyword: value})
                process = self.cli(flag, str(value))
                self.assertEqual(process.returncode, 2, process.stderr)
                self.assertIn('together', process.stderr)

    def test_recorded_root_must_be_absolute_without_traversal(self):
        for root in ('', 'relative/root', str(self.recorded / '..' / 'ECHO-Station')):
            with self.subTest(root=root), self.assertRaisesRegex(ValueError, 'recorded root'):
                auditor.audit(self.journal, recorded_root=root, archive_root=self.archive)

    def test_missing_archive_files_never_fall_back_to_existing_originals(self):
        for path in (self.journal, self.checkpoint, self.source):
            with self.subTest(path=path):
                raw = path.read_bytes()
                path.unlink()
                try:
                    with self.forbid_original_access(), self.assertRaises(FileNotFoundError):
                        self.audit_archive()
                finally:
                    path.write_bytes(raw)

    def test_reference_paths_must_be_in_original_namespace_without_traversal(self):
        for field in ('source', 'checkpoint'):
            original = self.original_source if field == 'source' else self.original_checkpoint
            archived = self.source if field == 'source' else self.checkpoint
            sibling = self.recorded.with_name('ECHO-Station-other') / original.relative_to(self.recorded)
            sibling.parent.mkdir(parents=True, exist_ok=True)
            sibling.write_bytes(original.read_bytes())
            invalid_paths = (sibling, archived, original.relative_to(self.recorded),
                             original.parent / '..' / original.parent.name / original.name)
            for invalid in invalid_paths:
                with self.subTest(field=field, path=invalid):
                    self.data['slots'][0]['source'] = str(invalid if field == 'source' else self.original_source)
                    self.rows[0]['checkpoint'] = str(invalid if field == 'checkpoint' else self.original_checkpoint)
                    self.reseal_checkpoint()
                    with self.forbid_original_access(), self.assertRaisesRegex(ValueError, 'namespace|traversal'):
                        self.audit_archive()

    def test_checkpoint_must_still_be_beside_audited_journal(self):
        other = self.archive / 'Artifacts/other.json'
        other.write_bytes(self.checkpoint.read_bytes())
        self.rows[0]['checkpoint'] = str(self.recorded / 'Artifacts/other.json')
        write_journal(self.journal, self.rows)
        with self.assertRaises(AssertionError):
            self.audit_archive()

    def test_journal_outside_archive_is_rejected_before_read(self):
        outside = self.archive.with_name(self.archive.name + '-other') / 'thought.jsonl'
        outside.parent.mkdir()
        outside.write_bytes(self.journal.read_bytes())
        for path in (self.original_journal, outside):
            with self.subTest(path=path), self.forbid_original_access(), \
                    self.assertRaisesRegex(ValueError, 'archive'):
                self.audit_archive(path)

    def test_file_symlinks_cannot_escape_archive(self):
        outside = self.archive.with_name(self.archive.name + '-other')
        shutil.copytree(self.archive, outside)
        for target_root in (self.recorded, outside):
            for path in (self.journal, self.checkpoint, self.source):
                with self.subTest(path=path, target_root=target_root):
                    raw = path.read_bytes()
                    path.unlink()
                    path.symlink_to(target_root / path.relative_to(self.archive))
                    try:
                        with self.forbid_original_access(), self.assertRaisesRegex(ValueError, 'archive'):
                            self.audit_archive()
                    finally:
                        path.unlink()
                        path.write_bytes(raw)

    def test_archive_root_cannot_point_into_recorded_namespace(self):
        alias = self.root / 'live-alias'
        alias.symlink_to(self.recorded, target_is_directory=True)
        for archive in (self.recorded, self.recorded / 'nested-archive', alias):
            with self.subTest(archive=archive), self.forbid_original_access(), \
                    self.assertRaisesRegex(ValueError, 'archive root'):
                auditor.audit(self.journal, recorded_root=self.recorded, archive_root=archive)

    def test_directory_symlink_cannot_escape_archive(self):
        artifacts = self.archive / 'Artifacts'
        artifacts.rename(self.archive / 'saved-artifacts')
        artifacts.symlink_to(self.recorded / 'Artifacts', target_is_directory=True)
        with self.forbid_original_access(), self.assertRaisesRegex(ValueError, 'archive'):
            self.audit_archive()

    def test_symlinks_within_archive_are_allowed(self):
        target = self.source.with_name('saved-source.jsonl')
        self.source.rename(target)
        self.source.symlink_to(target.name)
        self.assertTrue(self.audit_archive()['valid'])

    def test_raw_tampering_still_breaks_each_hash_layer(self):
        changes = ((self.journal, b'"completed":true', b'"completed":false'),
                   (self.checkpoint, b'"destroyed":0', b'"destroyed":1'),
                   (self.source, b'"slot":0', b'"slot":1'))
        for path, before, after in changes:
            with self.subTest(path=path):
                raw = path.read_bytes()
                self.assertIn(before, raw)
                path.write_bytes(raw.replace(before, after))
                try:
                    with self.assertRaises(AssertionError):
                        self.audit_archive()
                finally:
                    path.write_bytes(raw)

    def test_resealed_checkpoint_must_match_journal_hash(self):
        self.data['extra'] = 'tampered'
        digest = hashlib.sha256(canonical(self.data, ascii=True)).hexdigest()
        self.checkpoint.write_bytes(canonical(dict(self.data, sha256=digest)))
        with self.assertRaises(AssertionError):
            self.audit_archive()

    def test_rehashed_source_must_still_match_checkpoint_receipt(self):
        self.source_rows[0]['extract'] = dict(self.receipt, outcome=dict(status='arrived'))
        write_journal(self.source, self.source_rows)
        with self.assertRaises(AssertionError):
            self.audit_archive()

    def test_rehashed_journal_must_still_verify_memory_read(self):
        self.rows[1]['verified'] = False
        write_journal(self.journal, self.rows)
        with self.assertRaises(AssertionError):
            self.audit_archive()

    def test_default_audit_still_rejects_tampering(self):
        self.original_source.write_bytes(self.original_source.read_bytes().replace(b'"slot":0', b'"slot":1'))
        with self.assertRaises(AssertionError):
            auditor.audit(self.original_journal)

    def test_station_dispatch_is_preserved(self):
        rows = [dict(kind='station_observation', observation=dict(seq=0)),
                dict(kind='station_basal_finish', result=dict(
                    status='closed', resolution='completed', completed=True,
                    basal=dict(camUsed=0, camDestroyed=0, E=[0, 20, 0, 0, 10, 0]),
                    budgetUsed=1, budgetRemaining=999_999))]
        write_journal(self.original_journal, rows)
        self.journal.write_bytes(self.original_journal.read_bytes())
        expected = auditor.audit(self.original_journal)
        self.assertEqual(expected['domain'], 'station')
        with self.forbid_original_access():
            self.assertEqual(self.audit_archive(), dict(expected, journal=str(self.journal)))


if __name__ == '__main__':
    unittest.main()
