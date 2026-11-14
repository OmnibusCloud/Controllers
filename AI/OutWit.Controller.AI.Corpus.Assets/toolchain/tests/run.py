#!/usr/bin/env python3
"""Runs a folder of PDFs through the extractor under the wasmtime command line, one instance
per document, and checks what comes out against the contract.

    run.py compile <wasmtime> <pdftext.wasm> <pdftext.cwasm>
    run.py <wasmtime> <pdftext.cwasm> <pdf-dir> <out-dir> [options]

    --set NAME          a name for the report (default: the folder's name)
    --expect FILE       per-document expectations (the contract set's expect.json)
    --fuel N            fuel per document (default 200e9)
    --memory BYTES      the linear-memory ceiling (default 1 GiB)
    --wall SECONDS      the wall-clock backstop per run (default 300): past it the run is
                        killed and the document fails the test as "hang"
    --jobs N            documents run in parallel (default 1)
    --measure-fuel      also find, by bisection under the command line, the least fuel each
                        finishing document needs (to 1 %)
    --texts             keep every text.txt in <out-dir>/texts-<set> (with --expect they
                        are kept anyway, to be checked)

The module is the precompiled one (wasmtime compile with the same -W options); the engine
options mirror the controller's sandbox: fuel, the memory ceiling (a grow past it is refused,
as in the sandbox, not trapped), NaN canonicalisation, an 8 MiB wasm stack, no threads,
exceptions, GC, memory64, multi-memory or components, deterministic relaxed SIMD. The guest sees its document as
/in/doc.pdf and an empty /out, nothing else, and runs as "pdftext /in/doc.pdf /out".

Every run ends in one verdict: ok (exit 0), refused:<code> (exit 2), trap, fuel (out of fuel),
or a failure of the test: hang, internal:<exit>, cli (the command line failed, not the
guest), contract (the outputs break the contract). Writes <out-dir>/<set>.jsonl and prints a
Markdown summary; the exit code is 1 when any document failed the test.
"""

import argparse
import concurrent.futures
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

# A grow past the memory ceiling is refused (memory.grow returns -1), not a trap: the
# sandbox's binding has no trap-on-grow-failure (the C API's store limiter has no such flag),
# so the command line runs as the sandbox does. The module then sees a failed allocation:
# where the library asks fallibly it goes on, otherwise the standard library prints
# "memory allocation of N bytes failed" and aborts (a trap).
ENGINE_OPTIONS = [
    "trap-on-grow-failure=n",
    "nan-canonicalization=y",
    "max-wasm-stack=8388608",
    "threads=n",
    "shared-memory=n",
    "exceptions=n",
    "gc=n",
    "memory64=n",
    "multi-memory=n",
    "component-model=n",
    "relaxed-simd-deterministic=y",
]
CODES = ("encrypted", "unreadable", "no_pages")
STACK_LIMIT = 64 << 20
FUEL_PRECISION = 1.01
FUEL_FLOOR = 100000


def engine_flags(fuel, memory):
    flags = ["-W", "fuel=%d" % fuel, "-W", "max-memory-size=%d" % memory]
    for option in ENGINE_OPTIONS:
        flags += ["-W", option]
    return flags


def raise_stack_limit():
    """The command line runs the guest on its main thread, and an 8 MiB wasm stack needs more
    than Linux's default 8 MiB main-thread stack under it: the limit is raised here, once,
    and every run inherits it."""
    if os.name != "posix":
        return
    import resource
    soft, hard = resource.getrlimit(resource.RLIMIT_STACK)
    if soft != resource.RLIM_INFINITY and soft < STACK_LIMIT and (hard == resource.RLIM_INFINITY or hard >= STACK_LIMIT):
        resource.setrlimit(resource.RLIMIT_STACK, (STACK_LIMIT, hard))


def compile_module(wasmtime, module, output):
    """wasmtime compile with the engine options the runs use (fuel on, any amount)."""
    command = [wasmtime, "compile"] + engine_flags(1, 1 << 30) + [module, "-o", output]
    return subprocess.call(command)


def run_once(wasmtime, module, pdf, fuel, memory, wall, keep_text=None):
    """One instance on one document; returns the record of what happened."""
    work = tempfile.mkdtemp(prefix="pdftext-")
    try:
        folder_in = os.path.join(work, "in")
        folder_out = os.path.join(work, "out")
        os.makedirs(folder_in)
        os.makedirs(folder_out)
        try:
            os.link(pdf, os.path.join(folder_in, "doc.pdf"))
        except OSError:
            shutil.copyfile(pdf, os.path.join(folder_in, "doc.pdf"))

        command = [wasmtime, "run", "--allow-precompiled"] + engine_flags(fuel, memory) + [
            "--dir", folder_in + "::/in", "--dir", folder_out + "::/out",
            "--argv0", "pdftext", module, "/in/doc.pdf", "/out"]
        stderr_path = os.path.join(work, "stderr.txt")
        with open(stderr_path, "wb") as stderr, open(os.devnull, "wb") as stdout:
            started = time.monotonic()
            process = subprocess.Popen(command, stdout=stdout, stderr=stderr, stdin=subprocess.DEVNULL)
            if os.name == "posix":
                status, peak_kb, killed = wait_posix(process, wall)
            else:
                status, peak_kb, killed = wait_other(process, wall)
            wall_ms = (time.monotonic() - started) * 1000.0

        with open(stderr_path, "rb") as handle:
            message = handle.read().decode("utf-8", "replace")
        record = {"exit": status, "wall_ms": round(wall_ms, 1), "peak_mb": None if peak_kb is None else round(peak_kb / 1024.0, 1)}
        if killed:
            record["verdict"] = "hang"
            return record
        classify(record, status, message, folder_out)
        if keep_text and record["verdict"] == "ok":
            shutil.copyfile(os.path.join(folder_out, "text.txt"), keep_text)
        return record
    finally:
        shutil.rmtree(work, ignore_errors=True)


def wait_posix(process, wall):
    deadline = time.monotonic() + wall
    delay = 0.002
    while True:
        pid, status, usage = os.wait4(process.pid, os.WNOHANG)
        if pid == process.pid:
            process.returncode = os.waitstatus_to_exitcode(status)
            return process.returncode, usage.ru_maxrss, False
        if time.monotonic() > deadline:
            process.kill()
            _, status, usage = os.wait4(process.pid, 0)
            process.returncode = os.waitstatus_to_exitcode(status)
            return process.returncode, usage.ru_maxrss, True
        time.sleep(delay)
        delay = min(delay * 2, 0.05)


def wait_other(process, wall):
    try:
        return process.wait(timeout=wall), None, False
    except subprocess.TimeoutExpired:
        process.kill()
        return process.wait(), None, True


def first_line(message, *markers):
    lines = [line.strip() for line in message.splitlines() if line.strip()]
    for line in lines:
        if any(marker in line for marker in markers):
            return line[:200]
    return lines[0][:200] if lines else ""


def innermost_cause(message):
    """The last "N: ..." line of the command line's error chain that is not a backtrace
    frame: what stopped the guest ("wasm trap: ...", "forcing trap when growing memory ...")."""
    causes = []
    for line in message.splitlines():
        head, _, rest = line.strip().partition(": ")
        if head.isdigit() and rest and "wasm function" not in rest and "<unknown>" not in rest:
            causes.append(rest)
    if causes:
        return causes[-1][:200]
    return first_line(message, "wasm trap", "panicked", "memory allocation")


def classify(record, status, message, folder_out):
    """The verdict of a finished run, and the contract checks on what it wrote."""
    result_path = os.path.join(folder_out, "result.json")
    text_path = os.path.join(folder_out, "text.txt")
    if status in (0, 2):
        problems = []
        try:
            with open(result_path, "rb") as handle:
                result = json.loads(handle.read().decode("utf-8"))
        except (OSError, ValueError) as error:
            record["verdict"] = "contract"
            record["problem"] = "result.json: %s" % error
            return
        if sorted(result) != ["encrypted", "error", "pages"] or not isinstance(result["pages"], int) or not isinstance(result["encrypted"], bool):
            problems.append("result.json fields %r" % result)
        record.update(pages=result.get("pages"), encrypted=result.get("encrypted"), error=result.get("error"))
        if status == 0:
            record["verdict"] = "ok"
            if result.get("error") is not None:
                problems.append("exit 0 with error %r" % result.get("error"))
            try:
                with open(text_path, "rb") as handle:
                    data = handle.read()
                text = data.decode("utf-8")
            except (OSError, UnicodeDecodeError) as error:
                problems.append("text.txt: %s" % error)
                text = None
            if text is not None:
                record["chars"] = len(text)
                record["text_sha256"] = hashlib.sha256(data).hexdigest()
                record["replacements"] = text.count("�")
                if "\r" in text:
                    problems.append("a CR in text.txt")
                if isinstance(result.get("pages"), int) and text.count("\f") != result["pages"] - 1:
                    problems.append("%d form feeds for %r pages" % (text.count("\f"), result.get("pages")))
                if result.get("pages", 0) < 1:
                    problems.append("exit 0 with no pages")
        else:
            error = result.get("error")
            record["verdict"] = "refused:%s" % error
            if error not in CODES:
                problems.append("exit 2 with error %r" % error)
            if os.path.exists(text_path):
                problems.append("text.txt written by a refused document")
        if problems:
            record["verdict"] = "contract"
            record["problem"] = "; ".join(problems)
        return

    if "all fuel consumed" in message:
        record["verdict"] = "fuel"
    elif "wasm trap" in message or "wasm backtrace" in message or "error while executing" in message:
        record["verdict"] = "trap"
        record["trap"] = innermost_cause(message)
        panic = first_line(message, "panicked at", "memory allocation of")
        if "panicked at" in panic or "memory allocation of" in panic:
            record["panic"] = panic
    elif status in (64, 66, 73) or (1 <= status <= 125 and "Error:" not in message):
        record["verdict"] = "internal:%d" % status
        record["problem"] = first_line(message, "pdftext")
    else:
        record["verdict"] = "cli"
        record["problem"] = first_line(message, "Error", "error")


def measure_fuel(wasmtime, module, pdf, memory, wall, full_fuel, reference):
    """The least fuel (to FUEL_PRECISION) under which the document still finishes as at full
    fuel. Every finishing probe must give the reference's verdict and text."""
    def finishes(fuel):
        record = run_once(wasmtime, module, pdf, fuel, memory, wall)
        if record["verdict"] == "fuel":
            return False
        if record["verdict"] != reference["verdict"] or record.get("text_sha256") != reference.get("text_sha256"):
            raise RuntimeError("at fuel %d the run gave %s/%s, at full fuel %s/%s" % (
                fuel, record["verdict"], record.get("text_sha256"), reference["verdict"], reference.get("text_sha256")))
        return True

    high = full_fuel
    low = 0
    probe = high // 8
    while probe >= FUEL_FLOOR:
        if finishes(probe):
            high = probe
            probe //= 8
        else:
            low = probe
            break
    if low == 0:
        low = FUEL_FLOOR
        if finishes(low):
            return low, 0
    probes = 0
    while high > low * FUEL_PRECISION:
        middle = int((high * low) ** 0.5)
        if middle <= low or middle >= high:
            break
        if finishes(middle):
            high = middle
        else:
            low = middle
        probes += 1
    return high, probes


def percentile(values, fraction):
    ordered = sorted(values)
    if not ordered:
        return None
    rank = max(0, min(len(ordered) - 1, int(round(fraction * (len(ordered) - 1)))))
    return ordered[rank]


def check_expectation(name, record, expected):
    problems = []
    for key in ("pages", "encrypted", "error"):
        if record.get(key) != expected[key]:
            problems.append("%s %r, expected %r" % (key, record.get(key), expected[key]))
    expected_verdict = "ok" if expected["exit"] == 0 else "refused:%s" % expected["error"]
    if record["verdict"] != expected_verdict:
        problems.append("verdict %s, expected %s" % (record["verdict"], expected_verdict))
    return problems


def check_text(text, expected):
    problems = []
    if "pages_text" in expected:
        pages = [page.strip(" \t\n") for page in text.split("\f")]
        if pages != expected["pages_text"]:
            problems.append("pages %r, expected %r" % (pages, expected["pages_text"]))
    for fragment in expected.get("contains", []):
        if fragment not in text:
            problems.append("%r missing" % fragment)
    for character in expected.get("absent", []):
        if character in text:
            problems.append("%r present" % character)
    return problems


def main(argv):
    if len(argv) == 5 and argv[1] == "compile":
        return compile_module(argv[2], argv[3], argv[4])
    raise_stack_limit()
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("wasmtime")
    parser.add_argument("module")
    parser.add_argument("pdf_dir")
    parser.add_argument("out_dir")
    parser.add_argument("--set")
    parser.add_argument("--expect")
    parser.add_argument("--fuel", type=int, default=200 * 10 ** 9)
    parser.add_argument("--memory", type=int, default=1 << 30)
    parser.add_argument("--wall", type=float, default=300.0)
    parser.add_argument("--jobs", type=int, default=1)
    parser.add_argument("--measure-fuel", action="store_true")
    parser.add_argument("--texts", action="store_true")
    options = parser.parse_args(argv[1:])

    name = options.set or os.path.basename(os.path.normpath(options.pdf_dir))
    os.makedirs(options.out_dir, exist_ok=True)
    tempfile.tempdir = os.path.join(options.out_dir, "work-" + name)
    os.makedirs(tempfile.tempdir, exist_ok=True)
    texts = os.path.join(options.out_dir, "texts-" + name)
    if options.texts or options.expect:
        os.makedirs(texts, exist_ok=True)
    expectations = {}
    if options.expect:
        with open(options.expect, encoding="utf-8") as handle:
            expectations = json.load(handle)
    files = sorted(entry for entry in os.listdir(options.pdf_dir) if entry.endswith(".pdf"))

    def one(file_name):
        pdf = os.path.join(options.pdf_dir, file_name)
        keep = os.path.join(texts, file_name[:-4] + ".txt") if (options.texts or options.expect) else None
        record = run_once(options.wasmtime, options.module, pdf, options.fuel, options.memory, options.wall, keep)
        record = dict({"file": file_name, "bytes": os.path.getsize(pdf)}, **record)
        if options.measure_fuel and record["verdict"] not in ("fuel", "hang", "cli"):
            try:
                record["fuel_needed"], record["fuel_probes"] = measure_fuel(options.wasmtime, options.module, pdf, options.memory, options.wall, options.fuel, record)
            except RuntimeError as error:
                record["verdict"] = "contract"
                record["problem"] = "not reproducible: %s" % error
        return record

    started = time.monotonic()
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, options.jobs)) as pool:
        records = list(pool.map(one, files))
    elapsed = time.monotonic() - started

    failures = []
    for record in records:
        problems = []
        if record["verdict"] in ("hang", "cli", "contract") or record["verdict"].startswith("internal:"):
            problems.append(record.get("problem", record["verdict"]))
        expected = expectations.get(record["file"])
        if options.expect and expected is None:
            problems.append("no expectation")
        if expected is not None:
            problems += check_expectation(record["file"], record, expected)
            if record["verdict"] == "ok":
                with open(os.path.join(texts, record["file"][:-4] + ".txt"), encoding="utf-8", newline="") as handle:
                    problems += check_text(handle.read(), expected)
        if problems:
            record["failed"] = "; ".join(problems)
            failures.append(record)

    with open(os.path.join(options.out_dir, name + ".jsonl"), "w", encoding="utf-8", newline="\n") as handle:
        for record in records:
            handle.write(json.dumps(record, sort_keys=True) + "\n")

    print(summary(name, records, failures, elapsed, options))
    return 1 if failures else 0


def summary(name, records, failures, elapsed, options):
    verdicts = {}
    for record in records:
        verdicts[record["verdict"]] = verdicts.get(record["verdict"], 0) + 1
    ok = [record for record in records if record["verdict"] == "ok"]
    lines = ["### %s: %d documents in %.1f s (fuel %d, memory %d MiB, jobs %d)" % (
        name, len(records), elapsed, options.fuel, options.memory >> 20, options.jobs), ""]
    if not records:
        return "\n".join(lines + ["no documents"])
    lines.append("verdicts: " + ", ".join("%s %d" % (key, verdicts[key]) for key in sorted(verdicts)))
    if ok:
        lines.append("text of 200 characters or more: %d; pages %d; characters %d; U+FFFD %d" % (
            sum(1 for record in ok if record.get("chars", 0) >= 200), sum(record.get("pages", 0) for record in ok),
            sum(record.get("chars", 0) for record in ok), sum(record.get("replacements", 0) for record in ok)))
        lines.append("encrypted and read: %d" % sum(1 for record in ok if record.get("encrypted")))
    walls = [record["wall_ms"] for record in records]
    lines.append("wall per run (ms): median %.0f, p99 %.0f, max %.0f" % (percentile(walls, 0.5), percentile(walls, 0.99), max(walls)))
    peaks = [record["peak_mb"] for record in records if record.get("peak_mb") is not None]
    if peaks:
        lines.append("peak RSS of the command line (MB): median %.0f, p99 %.0f, max %.0f" % (percentile(peaks, 0.5), percentile(peaks, 0.99), max(peaks)))
    fuels = [record["fuel_needed"] for record in records if "fuel_needed" in record]
    if fuels:
        lines.append("fuel needed (command line, to 1 %%), %d documents: median %.3g, p99 %.3g, max %.3g" % (
            len(fuels), percentile(fuels, 0.5), percentile(fuels, 0.99), max(fuels)))
    lines.append("")
    lines.append("| file | bytes | verdict | pages | chars | fuel needed | wall ms | peak MB | note |")
    lines.append("|---|---|---|---|---|---|---|---|---|")
    for record in records:
        lines.append("| %s | %d | %s | %s | %s | %s | %.0f | %s | %s |" % (
            record["file"], record["bytes"], record["verdict"], record.get("pages", ""), record.get("chars", ""),
            "%.3g" % record["fuel_needed"] if "fuel_needed" in record else "", record["wall_ms"],
            "" if record.get("peak_mb") is None else "%.0f" % record["peak_mb"],
            (record.get("failed") or record.get("trap") or record.get("problem") or "").replace("|", "/")))
    if failures:
        lines.append("")
        lines.append("**%d failed:** %s" % (len(failures), ", ".join(record["file"] for record in failures)))
    return "\n".join(lines)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
