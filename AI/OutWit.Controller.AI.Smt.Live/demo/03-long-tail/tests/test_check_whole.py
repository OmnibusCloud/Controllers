"""check.py of the long-tail kit: what it holds beyond the rows of a made-up run. The counts the
integrity document must give, the rows of no problem of the list, the seed, the set and the
module of the provenance, and the numbers of the whole kit EXPECTED.md says every run of the set
under the kit's options must show again.

    python -I -m unittest discover -s <kit>/tests -t <kit>/tests
"""

import contextlib
import hashlib
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

import test_check  # noqa: E402 (the made-up runs of the tests beside this one)


def provenance(set_sha256="5" * 64):
    return {"profile": {"module_sha256": check.module_sha256()}, "problem_set": {"sha256": set_sha256}}


def made_up():
    items = test_check.listing()
    return items, {item["id"]: test_check.answered(item) for item in items}


def whole_of(items, rows):
    """The numbers of the whole kit as a made-up run gives them: what check.py would hold it to."""
    return check.measure(items, rows)


class WholeTests(unittest.TestCase):

    def test_an_integrity_document_that_does_not_say_a_count_is_a_fault(self):
        items, rows = made_up()
        integrity = test_check.integrity()
        del integrity["no_result"]
        faults, _ = check.hold(items, rows, test_check.report(), integrity)
        self.assertEqual(faults, ["integrity: no count of no_result"])

    def test_a_row_that_is_not_of_the_list_is_a_fault(self):
        items, rows = made_up()
        rows["stray"] = dict(rows["p01"], id="stray")
        faults, _ = check.hold(items, rows, test_check.report(), test_check.integrity())
        self.assertEqual(faults, ["1 row(s) of the results are not of the list (stray)"])

    def test_another_seed_is_a_fault(self):
        items, rows = made_up()
        report = test_check.report()
        report["run"]["options"]["seed"] = 7
        faults, _ = check.hold(items, rows, report, test_check.integrity())
        self.assertEqual(faults, ["the run's seed is 7, the kit's 0"])

    def test_another_module_is_a_fault(self):
        items, rows = made_up()
        other = provenance()
        other["profile"]["module_sha256"] = "1" * 64
        faults, _ = check.hold(items, rows, test_check.report(), test_check.integrity(), other)
        self.assertEqual(faults, ["the run's module is %s, not the pinned %s" % ("1" * 64, check.module_sha256())])

    def test_the_whole_kit_is_held_to_its_set_and_its_numbers(self):
        items, rows = made_up()
        report, integrity = test_check.report(), test_check.integrity()
        whole = dict(whole_of(items, rows), set_sha256="5" * 64)

        faults, _ = check.hold(items, rows, report, integrity, provenance(), whole)
        self.assertEqual(faults, [])

        rows["p05"] = test_check.answered(items[5], verdict="unknown", reason="fuel")
        faults, _ = check.hold(items, rows, report, integrity, provenance("6" * 64), whole)
        self.assertIn("the run is of the set %s, not the kit's %s" % ("6" * 64, "5" * 64), faults)
        self.assertTrue(any(fault.startswith("verdicts: ") and "EXPECTED.md: " in fault for fault in faults), faults)
        self.assertTrue(any(fault.startswith("p05: unknown at ") for fault in faults), faults)

    def test_an_unknown_short_of_the_full_cap_is_a_fault_of_the_whole_kit(self):
        items, rows = made_up()
        report, integrity = test_check.report(), test_check.integrity()
        rows["p05"] = test_check.answered(items[5], verdict="unknown", reason="fuel")
        rows["p05"]["stats"]["fuel"] = 12345
        whole = dict(whole_of(items, rows), set_sha256="5" * 64)

        faults, _ = check.hold(items, rows, report, integrity, provenance(), whole)
        self.assertEqual(faults, ["p05: unknown at 12345 fuel, not at the hard cap's %d" % check.CAP_FUEL])

    def test_the_lists_sha256_is_the_one_its_page_states(self):
        with open(os.path.join(KIT, "kit-list.tsv"), "rb") as handle:
            digest = hashlib.sha256(handle.read()).hexdigest()
        with open(os.path.join(KIT, "EXPECTED.md"), encoding="utf-8") as handle:
            self.assertIn("SHA-256 `%s`" % digest, handle.read())

    def test_the_kits_numbers_are_the_ones_its_page_states(self):
        with open(os.path.join(KIT, "EXPECTED.md"), encoding="utf-8") as handle:
            page = handle.read()
        whole = check.WHOLE
        self.assertIn(whole["set_sha256"], page)
        self.assertIn("%s in all" % format(whole["fuel"], ","), page)
        self.assertIn("`ok` %s, `output_exceeded` %d" % (format(whole["statuses"]["ok"], ","), whole["statuses"]["output_exceeded"]), page)
        self.assertIn("sat %s, unsat %d, unknown %d" % (format(whole["verdicts"]["sat"], ","), whole["verdicts"]["unsat"], whole["verdicts"]["unknown"]), page)
        self.assertIn("validated %s, partial %d" % (format(whole["classes"]["validated"], ","), whole["classes"]["partial"]), page)
        self.assertIn("agree %s, disagree 0, not applicable %d" % (format(whole["expected"]["agree"], ","), whole["expected"]["n/a"]), page)
        self.assertIn("median %s, p10 %s, p90 %s" % whole["ratios"], page)
        bands = ", ".join("%s s %d/%d" % (band, decided, unknown) for band, (decided, unknown) in whole["bands"].items())
        self.assertIn(bands, page)


class MainWholeTests(unittest.TestCase):

    def test_main_holds_the_provenance(self):
        items, rows = made_up()
        with tempfile.TemporaryDirectory() as folder:
            test_check.MainTests.write(folder, list(rows.values()), items)
            other = provenance()
            other["profile"]["module_sha256"] = "1" * 64
            with open(os.path.join(folder, "provenance.json"), "w", encoding="utf-8") as handle:
                json.dump(other, handle)
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = check.main(["check.py", folder, "--list", os.path.join(folder, "list.tsv")])
        self.assertEqual(code, 1)
        self.assertIn("NOT HELD: the run's module is", output.getvalue())


if __name__ == "__main__":
    unittest.main()
