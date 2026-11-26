"""check.py of the long-tail kit on made-up runs: what it holds, and the faults it names.

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


def listing():
    rows = []
    for number in range(40):
        logic = "QF_LIA" if number < 20 else "QF_BV"
        rows.append({"id": "p%02d" % number, "logic": logic, "status": "sat" if number % 2 else "unsat",
                     "time_s": "%.3f" % (1.5 + number), "band": "1-2" if number == 0 else "20-40" if number > 20 else "10-20"})
    return rows


def answered(item, verdict=None, reason="none", validation=None, status="ok"):
    verdict = verdict or item["status"]
    return {"index": int(item["id"][1:]), "id": item["id"], "query": "check", "logic": item["logic"], "status": status,
            "code": "", "message": "", "verdict": verdict, "reason": reason, "expected": item["status"],
            "expected_agreement": "agree" if verdict == item["status"] else "disagree" if verdict in ("sat", "unsat") else "n/a",
            "validation": validation or ("validated" if verdict == "sat" else "none"), "validation_reason": "",
            "copies": 1, "agreed": True, "settled_by": "one_copy", "model_truncated": False,
            "stats": {"resource_count": 1000, "fuel": int(5e9 * float(item["time_s"])), "checks": 1}}


def report():
    return {"run": {"options": dict(check.OPTIONS, profile="z3-5.4.1", max_models=2, seed=0, fuel=1, sampling_rate=0.05)}}


def integrity():
    return {"compared": 2, "agreements": 2, "unsat_one_copy": 18, "lies": 0, "evaluator_solver_disagreements": 0,
            "no_result": 0, "disputes": 0, "disputed": 0, "repairs": 0, "node_faults": 0}


class HoldTests(unittest.TestCase):

    def run_hold(self, change=None, report_of=report, integrity_of=integrity):
        items = listing()
        rows = {item["id"]: answered(item) for item in items}
        if change:
            change(items, rows)
        return check.hold(items, rows, report_of(), integrity_of())

    def test_a_run_with_every_verdict_the_status_holds(self):
        faults, lines = self.run_hold()
        self.assertEqual(faults, [])
        text = "\n".join(lines)
        self.assertIn("QF_LIA        20    10     10        0   10/0/0", text)
        self.assertIn("validated: 20 of 20 sat rows (100.0 %)", text)
        self.assertIn("unknown  : 0, none", text)

    def test_unknown_at_a_budgets_end_holds(self):
        def change(items, rows):
            rows["p03"] = answered(items[3], verdict="unknown", reason="fuel")
            rows["p04"] = answered(items[4], verdict="unknown", reason="rlimit")
            rows["p06"] = answered(items[6], verdict="unknown", reason="memout")
        faults, lines = self.run_hold(change)
        self.assertEqual(faults, [])
        self.assertIn("unknown  : 3, fuel 1, memout 1, rlimit 1", "\n".join(lines))

    def test_the_opposite_verdict_is_a_fault(self):
        faults, _ = self.run_hold(lambda items, rows: rows.update(p02=answered(items[2], verdict="sat")))
        self.assertEqual(len(faults), 1)
        self.assertIn("p02 (QF_LIA): verdict sat, the problem's :status says unsat", faults[0])

    def test_unknown_for_another_reason_is_a_fault(self):
        faults, _ = self.run_hold(lambda items, rows: rows.update(p02=answered(items[2], verdict="unknown", reason="incomplete")))
        self.assertEqual(faults, ["p02 (QF_LIA): unknown for incomplete, which is no budget's end"])

    def test_a_failed_model_is_a_fault(self):
        faults, _ = self.run_hold(lambda items, rows: rows.update(p01=answered(items[1], validation="failed")))
        self.assertEqual(len(faults), 1)
        self.assertIn("p01 (QF_LIA): the evaluator calls the model failed", faults[0])

    def test_too_few_validated_models_is_a_fault(self):
        def change(items, rows):
            for item in items[1:5:2]:
                rows[item["id"]] = answered(item, validation="partial")
        faults, _ = self.run_hold(change)
        self.assertEqual(faults, ["the evaluator validated 18 of 20 sat rows, less than 95 %"])

    def test_one_partial_model_in_twenty_holds(self):
        faults, _ = self.run_hold(lambda items, rows: rows.update(p01=answered(items[1], validation="partial")))
        self.assertEqual(faults, [])

    def test_a_missing_row_or_an_unanswered_one_is_a_fault(self):
        def change(items, rows):
            del rows["p05"]
            rows["p07"] = answered(items[7], status="engine_crash")
            rows["p09"] = answered(items[9], status="output_exceeded")
        faults, _ = self.run_hold(change)
        self.assertEqual(len(faults), 2)
        self.assertEqual(faults[0], "p05: no row")
        self.assertTrue(faults[1].startswith("p07: engine_crash"))

    def test_other_options_are_a_fault(self):
        def other():
            value = report()
            value["run"]["options"]["hard_cap_s"] = 60
            return value
        faults, _ = self.run_hold(report_of=other)
        self.assertEqual(faults, ["the run's hard_cap_s is 60, the kit's %d" % check.OPTIONS["hard_cap_s"]])

    def test_a_lie_or_a_standing_dispute_is_a_fault(self):
        def bad():
            value = integrity()
            value["lies"] = 1
            value["disputed"] = 2
            return value
        faults, _ = self.run_hold(integrity_of=bad)
        self.assertEqual(faults, ["integrity: lies 1", "integrity: disputed 2"])


class MainTests(unittest.TestCase):

    @staticmethod
    def write(folder, rows, items):
        with open(os.path.join(folder, "list.tsv"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("id\tlogic\tstatus\ttime_s\tband\n")
            for item in items:
                handle.write("%s\t%s\t%s\t%s\t%s\n" % (item["id"], item["logic"], item["status"], item["time_s"], item["band"]))
        with open(os.path.join(folder, "results.jsonl"), "w", encoding="utf-8", newline="\n") as handle:
            for row in rows:
                handle.write(json.dumps(row) + "\n")
        with open(os.path.join(folder, "report.json"), "w", encoding="utf-8") as handle:
            json.dump(report(), handle)
        with open(os.path.join(folder, "integrity.json"), "w", encoding="utf-8") as handle:
            json.dump({"summary": integrity()}, handle)
        with open(os.path.join(folder, "provenance.json"), "w", encoding="utf-8") as handle:
            json.dump({"profile": {"module_sha256": check.module_sha256()}, "problem_set": {"sha256": "5" * 64}}, handle)

    def test_the_exit_code_says_whether_everything_held(self):
        items = listing()
        with tempfile.TemporaryDirectory() as folder:
            self.write(folder, [answered(item) for item in items], items)
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = check.main(["check.py", folder, "--list", os.path.join(folder, "list.tsv")])
            self.assertEqual(code, 0)
            self.assertIn("held: everything", output.getvalue())

            rows = [answered(item) for item in items]
            rows[2] = answered(items[2], verdict="sat")
            self.write(folder, rows, items)
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = check.main(["check.py", folder, "--list", os.path.join(folder, "list.tsv")])
            self.assertEqual(code, 1)
            self.assertIn("NOT HELD: p02", output.getvalue())
            self.assertIn("held: NO, 1 fault(s)", output.getvalue())


if __name__ == "__main__":
    unittest.main()
