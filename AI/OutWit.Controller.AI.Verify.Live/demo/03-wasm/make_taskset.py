"""Writes the raw-wasm kit's taskset.jsonl and candidates.csv from the compiled modules.

The modules are what the controller's toolchain builds from src/ (toolchain/build.sh, kit-*.wasm):
the build server rebuilds them, runs this script on what it built and compares the taskset with
the one committed here, byte for byte. Run it again on the same modules and the files do not
change.

usage: python make_taskset.py <modules folder>
"""
import base64
import csv
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

SUITES = {
    "sum": [
        {"stdin": "1 2 3", "expected_stdout": "6\n"},
        {"stdin": "", "expected_stdout": "0\n"},
        {"stdin": "3000000000 4000000000", "expected_stdout": "7000000000\n"},
        {"stdin": "-5 5 10", "expected_stdout": "10\n"},
    ],
    "primes": [
        {"args": ["10"], "expected_stdout": "4\n"},
        {"args": ["13"], "expected_stdout": "6\n"},
        {"args": ["1000000"], "expected_stdout": "78498\n"},
    ],
    "words": [
        {"stdin": "the cat and the hat", "expected_stdout": "the 2\n"},
        {"stdin": "B a b A", "expected_stdout": "a 2\n"},
        {"stdin": "x", "expected_stdout": "x 1\n"},
    ],
    "edges": [
        {"expected_stdout": "0\n"},
    ],
}

# problem, candidate, module, language, the verdict it gets, why
CANDIDATES = [
    ("sum", "c", "kit-sum-c.wasm", "C", "Pass", "64-bit total"),
    ("sum", "rust", "kit-sum-rust.wasm", "Rust", "Pass", "64-bit total"),
    ("sum", "c-int-total", "kit-sum_int-c.wasm", "C", "Fail", "an int total wraps past 2^31"),
    ("sum", "rust-unwrap", "kit-sum_unwrap-rust.wasm", "Rust", "RuntimeError", "parses as i32 and unwraps: a panic"),
    ("primes", "c", "kit-primes-c.wasm", "C", "Pass", "a sieve"),
    ("primes", "rust", "kit-primes-rust.wasm", "Rust", "Pass", "a sieve"),
    ("primes", "c-naive", "kit-primes_naive-c.wasm", "C", "Timeout", "trial division by every smaller number"),
    ("primes", "rust-below", "kit-primes_below-rust.wasm", "Rust", "Fail", "leaves the limit itself out"),
    ("words", "rust", "kit-words-rust.wasm", "Rust", "Pass", "case-insensitive"),
    ("words", "c", "kit-words-c.wasm", "C", "Pass", "case-insensitive"),
    ("words", "c-case", "kit-words_case-c.wasm", "C", "Fail", "keeps the case as written"),
    ("edges", "c-network", "kit-net-c.wasm", "C", "CompileError", "imports a host function the sandbox does not provide"),
    ("edges", "c-threads", "kit-threads-c.wasm", "C", "CompileError", "a shared memory: threads are not offered"),
]

# The budget of every task: the naive count spends it, the others use a fraction.
LIMITS = {"fuel": 5_000_000_000}


def main(modules):
    tasks = []
    for index, (problem, candidate, module, _, _, _) in enumerate(CANDIDATES):
        with open(os.path.join(modules, module), "rb") as f:
            data = base64.b64encode(f.read()).decode("ascii")
        tasks.append({"index": index, "runtime": "wasi", "entry": f"{problem}-{candidate}.wasm",
                      "sources": {f"{problem}-{candidate}.wasm": data}, "suite": SUITES[problem], "limits": LIMITS})

    with open(os.path.join(HERE, "taskset.jsonl"), "w", encoding="utf-8", newline="\n") as out:
        for task in tasks:
            out.write(json.dumps(task, separators=(",", ":")) + "\n")

    with open(os.path.join(HERE, "candidates.csv"), "w", encoding="utf-8", newline="\n") as out:
        writer = csv.writer(out, lineterminator="\n")
        writer.writerow(["index", "problem", "candidate", "language", "verdict", "why"])
        for index, row in enumerate(CANDIDATES):
            writer.writerow([index, *row[:2], row[3], row[4], row[5]])

    print(f"{len(tasks)} tasks")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1])
