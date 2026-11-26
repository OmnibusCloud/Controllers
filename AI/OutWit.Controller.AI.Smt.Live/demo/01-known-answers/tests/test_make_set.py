"""make_set.py of the known-answers kit without the network or the tool: the fetcher and the pack
are stood in for, and the set they leave is held to the kit's SHA-256.

    python -I -m unittest discover -s <kit>/tests -t <kit>/tests
"""

import contextlib
import importlib.util
import io
import os
import sys
import tempfile
import unittest
from unittest import mock

KIT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(KIT, name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


make_set = load("make_set")


class MakeSetTests(unittest.TestCase):

    def run_main(self, rows):
        """Runs make_set with a fetcher that fetches nothing and a pack that writes the rows given."""
        calls = []

        def call(command):
            calls.append(command)
            if "pack" in command:
                with open(command[command.index("--out") + 1], "w", encoding="utf-8", newline="\n") as handle:
                    handle.writelines(rows)
            return 0

        with tempfile.TemporaryDirectory() as work:
            output = io.StringIO()
            with mock.patch.object(make_set.subprocess, "call", call), contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                code = make_set.main(["make_set.py", work, "--tool", sys.executable])
        return code, output.getvalue(), calls

    def test_a_set_of_other_bytes_is_refused_and_its_sha256_said(self):
        code, output, calls = self.run_main(['{"id":"p%03d","smt2":"(assert true)"}\n' % number for number in range(make_set.PROBLEMS)])

        self.assertEqual(len(calls), 2, "the fetcher and the pack ran")
        self.assertEqual(code, 1)
        self.assertIn("not the kit's %s" % make_set.SET_SHA256, output)

    def test_a_set_of_another_number_of_rows_is_refused(self):
        code, output, _ = self.run_main(['{"id":"p000","smt2":"(assert true)"}\n'])

        self.assertEqual(code, 1)
        self.assertIn("1 rows, not %d" % make_set.PROBLEMS, output)

    def test_the_set_is_described_by_its_rows_bytes_and_sha256(self):
        with tempfile.TemporaryDirectory() as work:
            path = os.path.join(work, "set.jsonl")
            with open(path, "wb") as handle:
                handle.write(b'{"id":"a"}\n\n{"id":"b"}\n')
            rows, size, sha256 = make_set.describe(path)
        self.assertEqual((rows, size), (2, 23))
        self.assertEqual(sha256, "f513aa3fc88c52841f1d1e214217975e556758ba5df94090ecaf5233e2111d60")

    def test_the_kits_page_names_the_set_make_set_holds(self):
        with open(os.path.join(KIT, "EXPECTED.md"), encoding="utf-8") as handle:
            page = handle.read()
        self.assertIn(make_set.SET_SHA256, page)
        self.assertIn("%d rows, %s bytes" % (make_set.PROBLEMS, format(make_set.SET_BYTES, ",")), page)


if __name__ == "__main__":
    unittest.main()
