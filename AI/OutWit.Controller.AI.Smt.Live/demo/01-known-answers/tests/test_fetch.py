"""The slice fetcher this kit uses (AI/OutWit.Controller.AI.Smt.Tests/Slice/fetch.py), without the
network: a file that is already there is taken only when it is the list's, and a fetched file is
written whole or not at all.

    python -I -m unittest discover -s <kit>/tests -t <kit>/tests
"""

import hashlib
import importlib.util
import os
import tempfile
import unittest

KIT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FETCHER = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(KIT))), "OutWit.Controller.AI.Smt.Tests", "Slice", "fetch.py")


def load():
    spec = importlib.util.spec_from_file_location("fetch", FETCHER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fetch = load()


def sha(data):
    return hashlib.sha256(data).hexdigest()


class FetchTests(unittest.TestCase):

    def test_a_file_already_there_is_kept_only_when_it_is_the_lists(self):
        members = {
            "non-incremental/QF_LIA/a.smt2": {"id": "p000", "sha256": sha(b"(assert true)")},
            "non-incremental/QF_LIA/b.smt2": {"id": "p001", "sha256": sha(b"(assert false)")},
            "non-incremental/QF_LIA/c.smt2": {"id": "p002", "sha256": sha(b"(check-sat)")},
        }
        with tempfile.TemporaryDirectory() as out:
            with open(os.path.join(out, "p000.smt2"), "wb") as handle:
                handle.write(b"(assert true)")
            with open(os.path.join(out, "p001.smt2"), "wb") as handle:
                handle.write(b"(assert fal")
            missing = fetch.missing_members(members, out)
        self.assertEqual(missing, {"non-incremental/QF_LIA/b.smt2", "non-incremental/QF_LIA/c.smt2"},
                         "a short file and an absent one are fetched; the file of the list is kept")

    def test_a_fetched_file_is_written_whole_or_not_at_all(self):
        with tempfile.TemporaryDirectory() as out:
            fetch.keep(out, {"id": "p007"}, b"(assert true)")
            self.assertEqual(sorted(os.listdir(out)), ["p007.smt2"], "no .part is left")
            with open(os.path.join(out, "p007.smt2"), "rb") as handle:
                self.assertEqual(handle.read(), b"(assert true)")


if __name__ == "__main__":
    unittest.main()
