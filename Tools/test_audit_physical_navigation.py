"""Tamper tests using an explicitly supplied completed physical run as a fixture.

python3 -B Tools/test_audit_physical_navigation.py --root ARTIFACT_ROOT
Every mutation is in an isolated temporary copy; no simulator/backend is launched.
"""

import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import audit_physical_navigation as auditor


def rechain(rows):
    previous = "0" * 64
    for index, row in enumerate(rows):
        row.pop("hash", None)
        row.update(index=index, prev=previous)
        raw = json.dumps(row, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)
        previous = hashlib.sha256(raw.encode()).hexdigest()
        row["hash"] = previous
    return "".join(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n" for row in rows)


def relocate(value, source, target):
    if isinstance(value, str):
        return str(target) + value[len(str(source)):] if value.startswith(str(source) + "/") else value
    if isinstance(value, dict):
        return {key: relocate(item, source, target) for key, item in value.items()}
    if isinstance(value, list):
        return [relocate(item, source, target) for item in value]
    return value


class TamperTests(unittest.TestCase):
    source = receipt_source = None
    recorded_root = None

    @classmethod
    def setUpClass(cls):
        baseline = auditor.audit(cls.source, cls.receipt_source, cls.recorded_root)
        if not baseline["valid"]:
            raise AssertionError("A passing physical fixture is required: " + json.dumps(baseline["errors"]))
        cls.original_model = json.loads((cls.source / "model.json").read_text())
        cls.original_receipt = json.loads(cls.receipt_source.read_text())
        cls.original_journals = {}
        for path in cls.source.glob("run-*/thought.jsonl"):
            cls.original_journals[path.relative_to(cls.source)] = [json.loads(line) for line in path.read_text().splitlines()]
        # Only in-memory test fixtures are relocated; archived hashes stay untouched.
        if cls.recorded_root is not None:
            cls.original_model = relocate(cls.original_model, cls.recorded_root, cls.source)
            cls.original_receipt = relocate(cls.original_receipt, cls.recorded_root, cls.source)
            cls.original_journals = {path: relocate(rows, cls.recorded_root, cls.source)
                                     for path, rows in cls.original_journals.items()}

    def setUp(self):
        # Test artifacts stay in this project and are removed even after failure.
        temporary = tempfile.TemporaryDirectory(prefix="physical-audit-tamper-", dir=auditor.PROJECT / "Logs")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / "namespace"
        self.root.mkdir()
        self.receipt = Path(temporary.name) / "integration.json"
        self.integration = copy.deepcopy(self.original_receipt)
        self.receipt.write_text(json.dumps(self.integration))
        self.model = relocate(copy.deepcopy(self.original_model), self.source, self.root)
        self.write_model()
        self.journals = {}
        for relative, original in self.original_journals.items():
            path = self.root / relative
            path.parent.mkdir()
            rows = relocate(copy.deepcopy(original), self.source, self.root)
            path.write_text(rechain(rows))
            self.journals[rows[0]["session"]] = (path, rows)

    def write_model(self):
        (self.root / "model.json").write_text(json.dumps(self.model))

    def session(self, index=0):
        return self.journals[self.integration["runs"][index]["session"]]

    def event(self, kind, index=0, predicate=lambda row: True):
        path, rows = self.session(index)
        return path, rows, next(row for row in rows if row["kind"] == kind and predicate(row))

    def rejected(self, message=None):
        result = auditor.audit(self.root, self.receipt)
        self.assertFalse(result["valid"], "tampered evidence passed")
        self.assertTrue(result["errors"])
        if message:
            self.assertIn(message, str(result["errors"]))

    def test_untampered_relocated_evidence_passes(self):
        result = auditor.audit(self.root, self.receipt)
        self.assertTrue(result["valid"], result["errors"])
        self.assertEqual(result["modelHash"], self.model["checkpoint"]["sha256"])
        self.assertGreaterEqual(result["runs"][0]["acceptedProjections"], 8)
        self.assertGreater(result["plansReplayedWithoutPriors"], 0)
        self.assertTrue(result["coreRestoreExact"])

    def test_raw_journal_edit_breaks_hash_chain(self):
        path, rows, event = self.event("navigation_decision")
        event["decision"]["reason"] = "unsealed edit"
        path.write_text("".join(json.dumps(row) + "\n" for row in rows))
        self.rejected("journal hash mismatch")

    def test_rehashed_projection_is_still_bound_to_actuation(self):
        path, rows, event = self.event("navigation_calibration")
        event["after"]["position"][0] += 1
        path.write_text(rechain(rows))
        self.rejected("projection not linked")

    def test_rehashed_projection_boolean_is_not_integer_zero(self):
        path, rows, event = self.event("navigation_calibration")
        event["before"]["goal"][0] = False
        path.write_text(rechain(rows))
        self.rejected("projection not linked")

    def test_rehashed_actuator_receipt_requires_exact_proposal(self):
        path, rows, event = self.event("navigation_observation", predicate=lambda r: r["observation"]["outcome"]["present"])
        event["observation"]["outcome"]["seq"] += 1
        path.write_text(rechain(rows))
        self.rejected("receipt not linked")

    def test_rehashed_arrival_outside_tolerance_cannot_learn(self):
        path, rows, event = self.event("navigation_observation", predicate=lambda r: r["observation"]["outcome"]["status"] == "arrived")
        event["observation"]["outcome"]["distanceMm"] = 201
        path.write_text(rechain(rows))
        self.rejected("non-unit movement")

    def test_rehashed_duplicate_projection_cannot_increase_support(self):
        path, rows, event = self.event("navigation_calibration", index=1, predicate=lambda r: not r["accepted"])
        event["accepted"] = True
        path.write_text(rechain(rows))
        self.rejected("duplicate physical projection")

    def test_resealed_checkpoint_support_requires_core_replay(self):
        checkpoint = self.model["checkpoint"]
        checkpoint["payload"]["model"]["support"][0] += 1
        checkpoint["sha256"] = hashlib.sha256(auditor.canonical(checkpoint["payload"]).encode()).hexdigest()
        self.write_model()
        self.rejected("disagrees with replayed evidence")

    def test_duplicate_checkpoint_evidence_rejected_by_core_restore(self):
        checkpoint = self.model["checkpoint"]
        checkpoint["payload"]["evidence"].append(copy.deepcopy(checkpoint["payload"]["evidence"][0]))
        checkpoint["sha256"] = hashlib.sha256(auditor.canonical(checkpoint["payload"]).encode()).hexdigest()
        self.write_model()
        self.rejected("duplicate transition receipt")

    def test_warm_started_first_learning_is_rejected(self):
        path, rows = self.session()
        rows[0].update(restored=True, modelHash=self.model["checkpoint"]["sha256"], support=self.model["checkpoint"]["payload"]["model"]["support"])
        path.write_text(rechain(rows))
        self.rejected("first acquisition must be empty")

    def test_missing_model_save_event_cannot_claim_persistence(self):
        path, rows = self.session()
        rows = [row for row in rows if row["kind"] != "navigation_model_saved"]
        path.write_text(rechain(rows))
        self.rejected("modelSaved")

    def test_saved_provenance_requires_physical_receipts(self):
        self.model["acquisition"][0]["calibrations"][0]["outcome"]["fromX"] += 1
        self.write_model()
        self.rejected("saved acquisition not linked")

    def test_trained_hash_cannot_drift(self):
        path, rows, event = self.event("navigation_decision", index=4)
        event["decision"]["modelHash"] = "f" * 64
        path.write_text(rechain(rows))
        self.rejected("modelHash")

    def test_plan_with_prior_rejected_even_with_valid_hash_chain(self):
        path, rows, event = self.event("navigation_plan", index=4, predicate=lambda r: r["result"]["status"] == "planned")
        event["result"]["certainty"] = "conditional_on_declared_prior"
        path.write_text(rechain(rows))
        self.rejected("declared effect prior")

    def test_plan_must_reproduce_actual_core_output(self):
        path, rows, event = self.event("navigation_plan", index=4, predicate=lambda r: r["result"]["status"] == "planned")
        event["result"]["actions"][0] = (event["result"]["actions"][0] + 1) % 4
        path.write_text(rechain(rows))
        self.rejected("differs from ECHO core.plan")

    def test_malformed_plan_produces_json_failure_instead_of_exception(self):
        path, rows, event = self.event("navigation_plan", index=4)
        event["result"] = []
        path.write_text(rechain(rows))
        self.rejected()

    def test_terminal_pending_claim_is_not_success(self):
        path, rows = self.session(4)
        rows[-1]["result"]["pending"] = True
        path.write_text(rechain(rows))
        self.rejected("closed actual-success")

    def test_final_camera_pose_must_be_at_goal(self):
        path, rows, event = self.event("navigation_observation", index=4, predicate=lambda r: r["terminal"])
        for ray in event["observation"]["rays"]:
            ray["sx"] += 300
            ray["ex"] += 300
        path.write_text(rechain(rows))
        self.rejected("physical camera/goal pose")

    def test_unclosed_journal_is_incomplete(self):
        path, rows = self.session(4)
        path.write_text(rechain(rows[:-1]))
        self.rejected("not a closed session")

    def test_global_receipt_cannot_claim_unexecuted_actions(self):
        self.integration["runs"][4]["moves"] += 1
        self.receipt.write_text(json.dumps(self.integration))
        self.rejected("Unity counter mismatch")

    def test_foreign_namespace_journal_is_rejected(self):
        path, rows, event = self.event("navigation_decision", index=4)
        event["decision"]["journal"] = str(self.source / path.relative_to(self.root))
        path.write_text(rechain(rows))
        self.rejected("crosses artifact namespace")

    def test_hidden_mapping_cannot_enter_observation(self):
        path, rows, event = self.event("navigation_observation")
        event["observation"]["mapping"] = [[1, 0]] * 4
        path.write_text(rechain(rows))
        self.rejected("non-public/missing observation fields")

    def test_missing_unity_receipt_writes_failed_json_and_nonzero_exit(self):
        self.receipt.unlink()
        output = self.root.parent / "failed-audit.json"
        process = subprocess.run([sys.executable, "-B", str(Path(auditor.__file__)), "--root", str(self.root),
                                  "--integration", str(self.receipt), "--output", str(output)],
                                 capture_output=True, text=True, timeout=20)
        self.assertEqual(process.returncode, 1, process.stderr)
        result = json.loads(output.read_text())
        self.assertFalse(result["valid"])
        self.assertEqual(result["errors"][0]["type"], "FileNotFoundError")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--integration", type=Path, default=auditor.DEFAULT_RECEIPT)
    parser.add_argument("--recorded-root", type=Path)
    args, remaining = parser.parse_known_args()
    TamperTests.source = args.root.expanduser().resolve()
    TamperTests.receipt_source = args.integration.expanduser().resolve()
    TamperTests.recorded_root = args.recorded_root
    unittest.main(argv=[sys.argv[0]] + remaining)
