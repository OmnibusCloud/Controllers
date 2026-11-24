"""The selection of the long-tail kit on a catalog and an archive of a few made-up benchmarks.

    python -I -m unittest discover -s <kit>/tests -t <kit>/tests

No network, no catalog, no archive: the tables are written here in memory.
"""

import hashlib
import importlib.util
import io
import os
import random
import sqlite3
import tarfile
import tempfile
import unittest
from collections import Counter

KIT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(KIT, name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


make_list = load("make_list")

SCHEMA = """
create table Solvers(id integer primary key, name text, link text);
create table Evaluations(id integer primary key, name text, date date, link text, hardwareRevision int, wallclockLimit real, memoryLimit real);
create table SolverVariants(id integer primary key, fullName text, solver int, evaluation int);
create table Families(id integer primary key, name text, folderName text, date date, firstOccurrence date, benchmarkCount int);
create table Licenses(id integer primary key, name text, link text, spdxIdentifier text);
create table Benchmarks(id integer primary key, name text, family int, logic text, isIncremental bool, size int, compressedSize int,
    license int, generatedOn datetime, generatedBy text, generator text, timeLimit real, application text, description text,
    category text, passesDolmen bool, passesDolmenStrict bool, queryCount int);
create table Queries(id integer primary key, benchmark int, idx int, normalizedSize int, compressedSize int, assertsCount int,
    declareFunCount int, declareConstCount int, declareSortCount int, defineFunCount int, defineFunRecCount int,
    constantFunCount int, defineSortCount int, declareDatatypeCount int, maxTermDepth int, status text, inferredStatus text);
create table Results(id integer primary key, evaluation int, query int, solverVariant int, cpuTime real, wallclockTime real, status text);
"""


class Catalog:
    """A catalog of made-up benchmarks in memory, written the way the real one is."""

    def __init__(self):
        self.db = sqlite3.connect(":memory:")
        self.db.executescript(SCHEMA)
        self.db.executemany("insert into Solvers values (?, ?, '')", [(1, "Z3"), (2, "Z3alpha"), (3, "cvc5")])
        self.db.executemany("insert into Evaluations values (?, ?, ?, '', 2, 1200, 30)", [
            (10, "SMT-COMP 2018", "2018-07-14"), (11, "SMT-COMP 2022", "2022-08-10"), (12, "SMT Evaluation 2013", "2013-07-02")])
        self.db.executemany("insert into SolverVariants values (?, ?, ?, ?)", [
            (100, "z3-4.7.1", 1, 10), (101, "z3-4.8.17", 1, 11), (102, "z3-4.8.17-other", 1, 11), (103, "z3alpha", 2, 11),
            (104, "cvc5", 3, 11), (105, "z3-2013", 1, 12)])
        self.db.executemany("insert into Licenses values (?, ?, ?, ?)", [
            (1, "CC BY", "https://creativecommons.org/licenses/by/4.0/", "CC-BY-4.0"),
            (3, "CC BY-NC", "https://creativecommons.org/licenses/by-nc/4.0/", "CC-BY-NC-4.0")])
        self.families = {}
        self.next = 1

    def family(self, folder):
        if folder not in self.families:
            self.families[folder] = len(self.families) + 1
            self.db.execute("insert into Families values (?, ?, ?, null, null, 0)", (self.families[folder], folder, folder))
        return self.families[folder]

    def add(self, name, status="sat", logic="QF_LIA", folder="fam-a", size=1000, licence=1, inferred=None, incremental=False, queries=1, results=()):
        """A benchmark and its results: (variant, cpu, status) each; its member path."""
        number = self.next
        self.next += 1
        self.db.execute("insert into Benchmarks (id, name, family, logic, isIncremental, size, license, category, queryCount) values (?, ?, ?, ?, ?, ?, ?, 'industrial', ?)",
                        (number, name, self.family(folder), logic, incremental, size, licence, queries))
        self.db.execute("insert into Queries (id, benchmark, idx, status, inferredStatus) values (?, ?, 1, ?, ?)", (number, number, status, inferred))
        for variant, cpu, answer in results:
            evaluation = self.db.execute("select evaluation from SolverVariants where id = ?", (variant,)).fetchone()[0]
            self.db.execute("insert into Results (evaluation, query, solverVariant, cpuTime, wallclockTime, status) values (?, ?, ?, ?, ?, ?)",
                            (evaluation, number, variant, cpu, None if cpu is None else cpu + 0.5, answer))
        return make_list.member_path(logic, folder, name)


class RuleTests(unittest.TestCase):

    def candidates(self, catalog):
        counts = Counter()
        found = make_list.read_candidates(catalog.db, counts)
        return {item["member"]: item for item in found}, counts

    def test_z3s_newest_run_decides_the_time(self):
        catalog = Catalog()
        newest = catalog.add("a.smt2", results=[(100, 200.0, "sat"), (101, 12.5, "sat"), (104, 0.1, "sat")])
        older_solved = catalog.add("b.smt2", results=[(100, 5.0, "sat"), (101, 1200.0, "unknown")])
        found, counts = self.candidates(catalog)
        self.assertEqual(set(found), {newest})
        self.assertEqual(found[newest]["time_s"], 12.5)
        self.assertEqual(found[newest]["wall_s"], 13.0)
        self.assertEqual(found[newest]["time_source"], "SMT-COMP 2022 z3-4.8.17")
        self.assertEqual(found[newest]["band"], "10-20")
        self.assertEqual(counts[("QF_LIA", "4 Z3 did not solve it at its newest run")], 1)
        self.assertNotIn(older_solved, found)

    def test_the_fastest_z3_entry_that_answered_the_status(self):
        catalog = Catalog()
        member = catalog.add("a.smt2", status="unsat", results=[(101, 30.0, "unsat"), (102, 3.0, "unsat"), (103, 1.5, "unsat")])
        wrong_fast = catalog.add("b.smt2", status="unsat", results=[(101, 40.0, "unsat"), (102, 2.0, "sat")])
        found, _ = self.candidates(catalog)
        self.assertEqual(found[member]["time_s"], 3.0)
        self.assertEqual(found[member]["time_source"], "SMT-COMP 2022 z3-4.8.17-other")
        self.assertEqual(found[wrong_fast]["time_s"], 40.0)

    def test_solvers_built_on_z3_and_evaluations_without_cpu_time_do_not_count(self):
        catalog = Catalog()
        alpha = catalog.add("a.smt2", results=[(103, 10.0, "sat")])
        untimed = catalog.add("b.smt2", results=[(105, None, "sat")])
        evaluation_only = catalog.add("c.smt2", results=[(104, 10.0, "sat")])
        found, counts = self.candidates(catalog)
        self.assertEqual(found, {})
        self.assertEqual(counts[("QF_LIA", "3 Z3 never ran on it")], 3)
        self.assertTrue(alpha and untimed and evaluation_only)

    def test_each_reason_is_counted_once(self):
        catalog = Catalog()
        catalog.add("no-status.smt2", status="unknown", results=[(101, 10.0, "sat")])
        catalog.add("contradicted.smt2", status="sat", inferred="unsat", results=[(101, 10.0, "sat")])
        catalog.add("wrong.smt2", status="sat", results=[(101, 10.0, "unsat")])
        catalog.add("fast.smt2", results=[(101, 0.99, "sat")])
        catalog.add("slow.smt2", results=[(101, 60.01, "sat")])
        catalog.add("nc.smt2", licence=3, results=[(101, 10.0, "sat")])
        catalog.add("large.smt2", size=make_list.MAX_BYTES + 1, results=[(101, 10.0, "sat")])
        catalog.add("incremental.smt2", incremental=True, results=[(101, 10.0, "sat")])
        catalog.add("two-queries.smt2", queries=2, results=[(101, 10.0, "sat")])
        catalog.add("other-logic.smt2", logic="QF_LRA", results=[(101, 10.0, "sat")])
        edge_low = catalog.add("one.smt2", results=[(101, 1.0, "sat")])
        edge_high = catalog.add("sixty.smt2", size=make_list.MAX_BYTES, results=[(101, 60.0, "sat")])
        inferred_same = catalog.add("inferred.smt2", inferred="sat", results=[(101, 5.0, "sat")])
        found, counts = self.candidates(catalog)
        self.assertEqual(set(found), {edge_low, edge_high, inferred_same})
        self.assertEqual(found[edge_low]["band"], "1-2")
        self.assertEqual(found[edge_high]["band"], "40-60")
        lia = {reason: count for (logic, reason), count in counts.items() if logic == "QF_LIA"}
        self.assertEqual(lia, {
            "benchmarks": 10, "1 no declared status": 1, "2 the competitions' answers contradict the status": 1,
            "4 Z3 answered wrongly at its newest run": 1, "5 Z3 took under 1 s": 1, "5 Z3 took over 60 s": 1,
            "6 in the band, under another licence": 1, "7 in the band, larger than the size cap": 1})


class ArchiveTests(unittest.TestCase):

    @staticmethod
    def tar(files):
        buffer = io.BytesIO()
        with tarfile.open(fileobj=buffer, mode="w") as archive:
            for name, data in files.items():
                info = tarfile.TarInfo(name)
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
        buffer.seek(0)
        return buffer

    def test_the_scan_hashes_the_wanted_members_and_reads_their_status(self):
        files = {
            "non-incremental/QF_LIA/f/a.smt2": b"(set-info :status sat)\n(check-sat)\n",
            "non-incremental/QF_LIA/f/b.smt2": b"(set-info  :status   unsat )(check-sat)",
            "non-incremental/QF_LIA/f/c.smt2": b"(check-sat)",
            "non-incremental/QF_LIA/f/d.smt2": b"(set-info :status sat)",
        }
        found = make_list.scan(self.tar(files), {"non-incremental/QF_LIA/f/a.smt2", "non-incremental/QF_LIA/f/b.smt2", "non-incremental/QF_LIA/f/c.smt2"})
        self.assertEqual(sorted(found), sorted(list(files)[:3]))
        self.assertEqual(found["non-incremental/QF_LIA/f/a.smt2"][0], len(files["non-incremental/QF_LIA/f/a.smt2"]))
        self.assertEqual(found["non-incremental/QF_LIA/f/b.smt2"][2], "unsat")
        self.assertEqual(found["non-incremental/QF_LIA/f/c.smt2"][2], "")
        self.assertEqual(found["non-incremental/QF_LIA/f/a.smt2"][1], hashlib.sha256(files["non-incremental/QF_LIA/f/a.smt2"]).hexdigest())

    def test_a_candidate_is_matched_by_path_size_and_status(self):
        candidates = [
            {"logic": "QF_LIA", "member": "m/ok", "bytes": 10, "status": "sat"},
            {"logic": "QF_LIA", "member": "m/missing", "bytes": 10, "status": "sat"},
            {"logic": "QF_LIA", "member": "m/size", "bytes": 10, "status": "sat"},
            {"logic": "QF_LIA", "member": "m/status", "bytes": 10, "status": "sat"},
        ]
        members = {"m/ok": (10, "h1", "sat"), "m/size": (11, "h2", "sat"), "m/status": (10, "h3", "unsat")}
        counts = Counter()
        matched = make_list.match(candidates, members, counts)
        self.assertEqual([item["member"] for item in matched], ["m/ok"])
        self.assertEqual(matched[0]["sha256"], "h1")
        self.assertEqual(sum(counts.values()), 3)

    def test_the_members_table_reads_back_what_was_written(self):
        members = {"non-incremental/QF_BV/x/y z.smt2": (12, "ab" * 32, "unsat"), "non-incremental/QF_BV/a.smt2": (3, "cd" * 32, "")}
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "QF_BV-members.tsv")
            make_list.write_members(path, members)
            self.assertEqual(make_list.read_members(path), members)


def made_up(logic, family, status, seconds, number):
    member = make_list.member_path(logic, family, "%s-%d.smt2" % (status, number))
    return {"logic": logic, "family": family, "member": member, "bytes": 100 + number, "status": status, "inferred": None,
            "spdx": "CC-BY-4.0", "license": "https://creativecommons.org/licenses/by/4.0/", "category": "industrial",
            "time_s": seconds, "wall_s": seconds + 0.1, "time_source": "SMT-COMP 2022 z3", "band": make_list.band_of(seconds),
            "sha256": "%064x" % number}


class PickTests(unittest.TestCase):

    def setUp(self):
        self.saved = (make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP)

    def tearDown(self):
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = self.saved

    @staticmethod
    def pool():
        items = []
        number = 0
        times = (1.5, 3.0, 7.0, 15.0, 30.0, 50.0)
        for logic, families in (("QF_LIA", {"big": 120, "mid": 20, "small": 3}), ("QF_BV", {"huge": 200, "other": 40})):
            for family, count in families.items():
                for index in range(count):
                    number += 1
                    items.append(made_up(logic, family, "sat" if index % 3 else "unsat", times[index % len(times)], number))
        return items

    def test_a_family_gives_no_more_than_its_cap_and_small_families_give_all(self):
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = 120, 60, 30, 2
        rows = make_list.pick(self.pool())
        lia = Counter(row["family"] for row in rows if row["logic"] == "QF_LIA")
        bv = Counter(row["family"] for row in rows if row["logic"] == "QF_BV")
        self.assertEqual(lia, Counter({"big": 30, "mid": 20, "small": 3}))
        self.assertEqual(bv, Counter({"huge": 30, "other": 30}))
        self.assertEqual(len(rows), 113)

    def test_the_target_of_the_first_logic_and_the_rest_for_the_second(self):
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = 100, 40, 100, 2
        rows = make_list.pick(self.pool())
        self.assertEqual(sum(row["logic"] == "QF_LIA" for row in rows), 40)
        self.assertEqual(sum(row["logic"] == "QF_BV" for row in rows), 60)

    def test_the_rounds_spread_a_family_over_statuses_and_bands(self):
        # The big family has six cells (unsat in two bands, sat in four): its cap of 12 takes two of each.
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = 80, 40, 12, 1
        rows = [row for row in make_list.pick(self.pool()) if row["logic"] == "QF_LIA" and row["family"] == "big"]
        cells = Counter((row["status"], row["band"]) for row in rows)
        self.assertEqual(len(rows), 12)
        self.assertEqual(set(cells.values()), {2})
        self.assertEqual(len(set(row["band"] for row in rows)), 6)

    def test_the_same_candidates_in_any_order_give_the_same_list(self):
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = 150, 70, 40, 3
        first = make_list.pick(self.pool())
        shuffled = self.pool()
        random.Random(7).shuffle(shuffled)
        second = make_list.pick(shuffled)
        self.assertEqual([(row["id"], row["member"], row["sample"]) for row in first], [(row["id"], row["member"], row["sample"]) for row in second])

    def test_ids_follow_the_paths_and_the_sample_is_spread_over_the_times(self):
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = 150, 70, 40, 3
        rows = make_list.pick(self.pool())
        lia = [row for row in rows if row["logic"] == "QF_LIA"]
        self.assertEqual([row["id"] for row in lia], ["lia%04d" % number for number in range(len(lia))])
        self.assertEqual([row["member"] for row in lia], sorted(row["member"] for row in lia))
        self.assertTrue(all(row["id"].startswith("bv") for row in rows if row["logic"] == "QF_BV"))
        sample = Counter((row["logic"], row["status"]) for row in rows if row["sample"])
        self.assertEqual(sample, Counter({("QF_LIA", "sat"): 3, ("QF_LIA", "unsat"): 3, ("QF_BV", "sat"): 3, ("QF_BV", "unsat"): 3}))
        sat = sorted((row for row in lia if row["status"] == "sat"), key=lambda row: (row["time_s"], row["member"]))
        marked = [index for index, row in enumerate(sat) if row["sample"]]
        self.assertEqual(marked, [len(sat) // 6, len(sat) // 2, 5 * len(sat) // 6])

    def test_the_list_is_written_with_its_columns_and_lf(self):
        make_list.KIT_PROBLEMS, make_list.LOGIC_TARGET, make_list.FAMILY_CAP, make_list.SAMPLE_PER_GROUP = 10, 5, 5, 1
        rows = make_list.pick(self.pool())
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "kit-list.tsv")
            make_list.write_list(path, rows)
            with open(path, "rb") as handle:
                data = handle.read()
        self.assertNotIn(b"\r", data)
        lines = data.decode("utf-8").splitlines()
        self.assertEqual(lines[0].split("\t"), list(make_list.COLUMNS))
        self.assertEqual(len(lines), 11)
        first = dict(zip(make_list.COLUMNS, lines[1].split("\t")))
        self.assertEqual(first["id"], "lia0000")
        self.assertRegex(first["time_s"], r"^\d+\.\d{3}$")
        self.assertIn(first["sample"], ("0", "1"))

    def test_bands(self):
        self.assertEqual([make_list.band_of(value) for value in (1.0, 1.999, 2.0, 4.99, 5.0, 19.9, 20.0, 39.99, 40.0, 60.0)],
                         ["1-2", "1-2", "2-5", "2-5", "5-10", "10-20", "20-40", "20-40", "40-60", "40-60"])


if __name__ == "__main__":
    unittest.main()
