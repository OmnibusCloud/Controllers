"""make_list.py's scan of an archive's stream: a stream that ends inside the archive, even exactly
between two members, is not taken for the whole archive (its members' table would be written
short and read again by the next run).

    python -I -m unittest discover -s <kit>/tests -t <kit>/tests
"""

import importlib.util
import io
import os
import tarfile
import unittest

KIT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(KIT, name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


make_list = load("make_list")

FILES = {
    "non-incremental/QF_LIA/f/a.smt2": b"(set-info :status sat)\n(check-sat)\n",
    "non-incremental/QF_LIA/f/b.smt2": b"(set-info :status unsat)\n(check-sat)\n",
}


def archive():
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w", format=tarfile.USTAR_FORMAT) as tar:
        for name, data in FILES.items():
            info = tarfile.TarInfo(name)
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))
    return buffer.getvalue()


class ScanEndTests(unittest.TestCase):

    def test_the_whole_archive_is_scanned(self):
        found = make_list.scan(io.BytesIO(archive()), set(FILES))
        self.assertEqual(sorted(found), sorted(FILES))

    def test_a_stream_cut_between_two_members_is_not_the_whole_archive(self):
        data = archive()
        with tarfile.open(fileobj=io.BytesIO(data)) as tar:
            cut = tar.getmembers()[1].offset

        with self.assertRaises(SystemExit) as raised:
            make_list.scan(io.BytesIO(data[:cut]), set(FILES))
        self.assertIn("ended inside the archive", str(raised.exception))


if __name__ == "__main__":
    unittest.main()
