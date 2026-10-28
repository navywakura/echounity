"""The final media scan must obey the same resource gate as rendering."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from echo_flightlab import videos


class VideoProbeTests(unittest.TestCase):
    def test_probe_uses_bounded_executor_and_preserves_json_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            movie = Path(directory) / 'flight.mp4'
            movie.write_bytes(b'placeholder')
            expected = dict(codec_name='h264', nb_read_frames='24')

            def bounded(argv, log, ram):
                self.assertEqual(argv[0], 'ffprobe')
                self.assertEqual(ram, '1G')
                self.assertIn('-count_frames', argv)
                self.assertEqual(argv[argv.index('-threads') + 1], '1')
                Path(argv[argv.index('-o') + 1]).write_text(json.dumps(dict(streams=[expected])))

            with patch.object(videos, 'bounded', side_effect=bounded) as gate, patch.object(
                videos.subprocess, 'run', side_effect=AssertionError('unbounded media process')
            ):
                self.assertEqual(videos.probe(movie), expected)
            self.assertEqual(gate.call_count, 1)
            self.assertEqual(json.loads(movie.with_suffix('.probe.json').read_text())['streams'][0], expected)

    def test_probe_never_overwrites_previous_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            movie = Path(directory) / 'flight.mp4'
            report = movie.with_suffix('.probe.json')
            report.write_text('preserved')
            with patch.object(videos, 'bounded') as gate:
                with self.assertRaises(FileExistsError):
                    videos.probe(movie)
            gate.assert_not_called()
            self.assertEqual(report.read_text(), 'preserved')
