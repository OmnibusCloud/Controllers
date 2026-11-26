"""check.py of the known-answers kit on made-up runs of the committed list: what it holds, and the
faults it names. A made-up run answers every problem of the list with the list's verdict, with the
evaluator's classes the slice test pins, as a fleet of two machines at --rate 1 answers it.

    python -I -m unittest discover -s <kit>/tests -t <kit>/tests
"""

import contextlib
import importlib.util
import io
import json
import os
import tempfile
import unittest

KIT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(KIT, name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check = load("check")


def made_up_rows(listing, pinned):
    """Every problem answered as the list says, the sat ones classed as the slice test pins."""
    rows = {}
    left = {logic: list(classes) for logic, classes in pinned.items()}
    exceeded = False
    for index, item in enumerate(listing):
        verdict = item["status"]
        validation = "none"
        status = "ok"
        if verdict == "sat":
            counts = left[item["logic"]]
            position = next(at for at, count in enumerate(counts) if count > 0)
            counts[position] -= 1
            validation = ("validated", "partial", "failed")[position]
            if validation == "partial" and item["logic"] == "QF_BV" and not exceeded:
                status = "output_exceeded"
                exceeded = True
        rows[item["id"]] = {
            "index": index, "id": item["id"], "query": "check", "logic": item["logic"], "status": status, "code": "", "message": "",
            "verdict": verdict, "reason": "none", "expected": verdict, "expected_agreement": "agree",
            "validation": validation, "validation_reason": "", "copies": 2, "agreed": True, "settled_by": "copies",
            "model_truncated": False, "stats": {"resource_count": 100, "fuel": 1000, "checks": 1}}
    return rows


def report():
    options = dict(check.OPTIONS, profile="z3-5.4.1", max_models=2, fuel=500000000000)
    return {"run": {"options": options, "sampling": "as_asked", "sampling_rate": 1}}


def integrity():
    return {"compared": 469, "agreements": 469, "one_copy": 0, "lies": 0, "evaluator_solver_disagreements": 0, "no_result": 0,
            "disputes": 0, "disputed": 0, "repairs": 0, "node_faults": 0}


def provenance():
    return {"profile": {"module_sha256": check.module_sha256()}, "problem_set": {"sha256": check.SET_SHA256}}


class HoldTests(unittest.TestCase):

    def run_hold(self, change=None, report_of=report, integrity_of=integrity, provenance_of=provenance):
        listing = check.read_list()
        pinned = check.read_classes()
        rows = made_up_rows(listing, pinned)
        if change:
            change(listing, rows)
        return check.hold(listing, pinned, rows, report_of(), integrity_of(), provenance_of())

    def test_a_run_that_answers_as_the_list_holds(self):
        faults, lines = self.run_hold()
        self.assertEqual(faults, [])
        self.assertIn("QF_LIA         150    75     75      0   75/0/0", "\n".join(lines))

    def test_an_integrity_document_that_does_not_say_a_count_is_a_fault(self):
        def silent():
            value = integrity()
            del value["lies"]
            return value
        faults, _ = self.run_hold(integrity_of=silent)
        self.assertEqual(faults, ["integrity: no count of lies"])

    def test_a_row_that_is_not_of_the_list_is_a_fault(self):
        def change(listing, rows):
            rows["stray"] = dict(rows[listing[0]["id"]], id="stray", index=469)
        faults, _ = self.run_hold(change)
        self.assertEqual(faults, ["1 row(s) of the results are not of the list (stray)"])

    def test_a_run_of_another_set_or_module_is_a_fault(self):
        def other():
            return {"profile": {"module_sha256": "1" * 64}, "problem_set": {"sha256": "5" * 64}}
        faults, _ = self.run_hold(provenance_of=other)
        self.assertEqual(len(faults), 2)
        self.assertIn("the run is of the set %s, not the kit's %s" % ("5" * 64, check.SET_SHA256), faults)
        self.assertIn("the run's module is %s, not the pinned %s" % ("1" * 64, check.module_sha256()), faults)

    def test_other_options_are_a_fault(self):
        def other():
            value = report()
            value["run"]["options"]["hard_cap_s"] = 60
            return value
        faults, _ = self.run_hold(report_of=other)
        self.assertEqual(faults, ["the run's hard_cap_s is 60, the kit's 100"])

    def test_statuses_other_than_the_pages_are_a_fault(self):
        def change(listing, rows):
            row = next(row for row in rows.values() if row["status"] == "ok" and row["verdict"] == "sat")
            row["status"] = "output_exceeded"
        faults, _ = self.run_hold(change)
        self.assertEqual(faults, ["statuses: ok 467, output_exceeded 2; EXPECTED.md: ok 468, output_exceeded 1"])

    def test_a_problem_that_rests_on_one_machine_is_a_fault(self):
        def change(listing, rows):
            rows[listing[3]["id"]]["settled_by"] = "one_copy"
        faults, _ = self.run_hold(change)
        self.assertEqual(faults, ["settled by: copies 468, one_copy 1; EXPECTED.md: copies 469"])

    def test_a_sampling_that_did_not_happen_is_a_fault(self):
        def short():
            value = report()
            value["run"]["sampling"] = "short"
            return value
        faults, _ = self.run_hold(report_of=short)
        self.assertEqual(faults, ["the sampling is short, not as_asked"])


class MainTests(unittest.TestCase):

    def test_the_exit_code_says_whether_everything_held(self):
        listing = check.read_list()
        rows = made_up_rows(listing, check.read_classes())
        with tempfile.TemporaryDirectory() as folder:
            with open(os.path.join(folder, "results.jsonl"), "w", encoding="utf-8", newline="\n") as handle:
                for row in rows.values():
                    handle.write(json.dumps(row) + "\n")
            for name, value in (("report.json", report()), ("integrity.json", {"summary": integrity()}), ("provenance.json", provenance())):
                with open(os.path.join(folder, name), "w", encoding="utf-8") as handle:
                    json.dump(value, handle)
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = check.main(["check.py", folder])
        self.assertEqual(code, 0, output.getvalue())
        self.assertIn("held: everything", output.getvalue())


if __name__ == "__main__":
    unittest.main()
