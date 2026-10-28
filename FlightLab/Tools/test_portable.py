import os
from pathlib import Path
import subprocess
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
CLI=ROOT/'FlightLab/Tools/flightlab.py'

class PortableTests(unittest.TestCase):
    def test_help_runs_without_echo_or_unity(self):
        r=subprocess.run([sys.executable,str(CLI),'--help'],capture_output=True,text=True,env={**os.environ,'ECHOAI_SOURCE':'/missing-echo-source'})
        self.assertEqual(r.returncode,0,r.stderr)
        self.assertIn('verify',r.stdout)

    def test_explicit_missing_source_is_not_silently_replaced(self):
        r=subprocess.run([sys.executable,str(CLI),'tests'],capture_output=True,text=True,env={**os.environ,'ECHOAI_SOURCE':'/missing-echo-source'})
        self.assertNotEqual(r.returncode,0)
        self.assertIn('ECHOAI_SOURCE',r.stderr+r.stdout)

    def test_portable_public_source_runs_original_cpu_tests(self):
        r=subprocess.run([sys.executable,str(CLI),'tests'],capture_output=True,text=True)
        self.assertEqual(r.returncode,0,r.stdout+r.stderr)
        self.assertIn('Ran 31 tests',r.stdout+r.stderr)

    def test_full_verification_replays_without_new_flights(self):
        r=subprocess.run([sys.executable,str(CLI),'verify'],capture_output=True,text=True)
        self.assertEqual(r.returncode,0,r.stdout+r.stderr)
        self.assertIn('five original decision replays and physical audits',r.stdout)

if __name__=='__main__': unittest.main()
