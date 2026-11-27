"""PF-2 spike, isolation on every OS (question 5): a native kernel, and the whole comparator tree,
run raw and inside each OS's sandbox; the same probes (files, network, processes) inside each.

    isolation.py kernels --mode M --root <lean root> --job <dir with solution.ndjson, challenge.ndjson>
                         --theorems a,b --kernels nanoda-4,leanchecker,comparator --out <record.json>
                         [--sandbox <Pf2Sandbox.exe>] [--repeat 2] [--memory-gib 0]
    isolation.py probe   --mode M --job <dir> --outside <dir> --out <record.json>

Modes: raw (no sandbox); linux: bwrap (ours: only the toolchain read-only and the job folder
writable, every namespace unshared), bwrap-host-ro (comparator's own: `--ro-bind / /`);
macos: sandbox-exec (a deny-by-default profile: the toolchain and system libraries readable, the
job folder writable, no network); windows: job (a Job Object only), appcontainer (AppContainer +
Job Object), appcontainer-lpac (a less-privileged AppContainer). Linux caps are systemd scopes
(PF1_CGROUP=1), elsewhere tree.py's sampled kill, on Windows the Job Object's own.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import socket
import subprocess
import sys
import threading

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(HERE)), "pf1"))

from tree import run_tree  # noqa: E402
from common import comparator_command, comparator_config, kernel_command, lean_env, tool, write_json  # noqa: E402

STANDARD_AXIOMS = ["propext", "Quot.sound", "Classical.choice"]


def macos_profile(root: str, job: str, probe: bool, variant: str = "strict") -> str:
    """sandbox-exec profiles (SBPL; a later rule wins over an earlier one).

    strict:   deny by default; read the toolchain and the system's libraries, map them executable,
              write only the job folder, execute only the toolchain (and, for the probes, the
              system's command-line tools); no network.
    system:   the same, on top of Apple's own baseline (`system.sb`) instead of a hand-made one.
    denylist: allow by default; deny the network, reading the users' and volumes' folders, and
              writing anywhere but the job folder (weaker: what is not named is allowed)."""
    bins = ' (subpath "/bin") (subpath "/usr/bin")' if probe else ""
    execs = f'(subpath "{root}")' + bins
    if variant == "denylist":
        return f"""(version 1)
(allow default)
(deny network*)
(deny file-read* (subpath "/Users") (subpath "/Volumes") (subpath "/private/var/root") (subpath "/private/etc"))
(deny file-write*)
(allow file-read* (subpath "{root}") (subpath "{job}"))
(allow file-write* (subpath "{job}") (literal "/dev/null") (literal "/dev/dtracehelper"))
(deny process-exec)
(allow process-exec {execs})
"""
    base = '(import "system.sb")\n' if variant == "system" else ""
    return f"""(version 1)
(deny default)
{base}(allow process-fork)
(allow process-exec {execs})
(allow process-info* (target self))
(allow file-read-metadata)
(allow file-read* (literal "/") (subpath "/usr/lib") (subpath "/usr/share") (subpath "/System") (subpath "/Library/Apple")
  (subpath "/private/var/db/timezone") (subpath "/private/var/db/dyld") (subpath "/bin") (subpath "/usr/bin")
  (literal "/dev/null") (literal "/dev/zero") (literal "/dev/urandom") (literal "/dev/random") (literal "/dev/dtracehelper")
  (subpath "{root}") (subpath "{job}"))
(allow file-map-executable (subpath "/usr/lib") (subpath "/System") (subpath "/bin") (subpath "/usr/bin") (subpath "{root}"))
(allow file-write* (subpath "{job}") (literal "/dev/null") (literal "/dev/dtracehelper"))
(allow file-ioctl (literal "/dev/dtracehelper") (literal "/dev/null"))
(allow sysctl-read)
(allow mach-lookup (global-name "com.apple.system.opendirectoryd.libinfo") (global-name "com.apple.system.logger"))
(allow signal (target self))
(deny network*)
"""


def wrap(mode: str, cmd: list, root: str, job: str, probe: bool = False) -> list:
    if mode == "raw" or mode.startswith("job") or mode.startswith("appcontainer"):
        return cmd
    if mode == "bwrap":
        argv = ["bwrap", "--unshare-all", "--die-with-parent", "--new-session", "--clearenv",
                "--ro-bind", "/usr", "/usr", "--symlink", "usr/lib", "/lib", "--symlink", "usr/lib64", "/lib64",
                "--symlink", "usr/bin", "/bin", "--symlink", "usr/sbin", "/sbin",
                "--ro-bind", "/etc/ld.so.cache", "/etc/ld.so.cache",
                "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp",
                "--ro-bind", root, root, "--bind", job, job, "--chdir", job,
                "--setenv", "PATH", f"{root}/bin:/usr/bin", "--setenv", "HOME", job, "--setenv", "LEAN_ABORT_ON_PANIC", "1"]
        return argv + ["--"] + cmd
    if mode == "bwrap-host-ro":
        # What `lake comparator` runs its kernels in: the host's whole file system read-only.
        return ["bwrap", "--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc", "--tmpfs", "/tmp",
                "--bind", job, job, "--unshare-all", "--die-with-parent", "--chdir", job, "--"] + cmd
    if mode == "sandbox-exec":
        profile = os.path.join(job, ("pf2-probe-" if probe else "pf2-kernel-") + os.environ.get("PF2_SB_VARIANT", "strict") + ".sb")
        with open(profile, "w", encoding="utf-8") as handle:
            handle.write(macos_profile(os.path.realpath(root), os.path.realpath(job), probe,
                                       os.environ.get("PF2_SB_VARIANT", "strict")))
        return ["sandbox-exec", "-f", profile] + cmd
    raise ValueError(mode)


def windows_run(sandbox: str, mode: str, cmd: list, root: str, job: str, cap_min: float, memory_gib: float,
                children: bool) -> dict:
    argv = [sandbox, "run", "--job-dir", job, "--grant-read", root, "--wall-s", str(int(cap_min * 60)),
            "--processes", "8", "--env", "LEAN_ABORT_ON_PANIC=1"]
    if memory_gib:
        argv += ["--memory-mb", str(int(memory_gib * 1024))]
    if mode == "job":
        argv.append("--no-container")
    if mode == "appcontainer-lpac":
        argv.append("--lpac")
    if not children:
        argv.append("--no-children")
    else:
        # `lake comparator` will not start without git on PATH, even when it judges two exports
        # and builds nothing: the folder of the host's git goes on the sandbox's PATH.
        git = shutil.which("git")
        system = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32")
        path = os.pathsep.join([os.path.join(root, "bin")] + ([os.path.dirname(git)] if git else []) + [system])
        argv += ["--env", f"PATH={path}"]
    row = run_tree(argv + ["--"] + cmd, timeout=cap_min * 60 + 120)
    text = row.pop("stdout_tail", "")
    try:
        result = json.loads(text[text.index("{"):])
    except ValueError:
        result = {"error": text[-800:], "says": row.get("says")}
    result["launcher_wall_s"] = row.get("wall_s")
    return result


def kernels(options) -> int:
    root = os.path.abspath(options.root)
    job = os.path.abspath(options.job)
    export = os.path.join(job, "solution.ndjson")
    challenge = os.path.join(job, "challenge.ndjson")
    record = json.load(open(options.out, encoding="utf-8")) if os.path.isfile(options.out) else {}
    env = lean_env(root, None)
    theorems = options.theorems.split(",")
    for kernel in [k for k in options.kernels.split(",") if k]:
        for attempt in range(options.repeat):
            if kernel == "comparator":
                config = comparator_config(os.path.join(job, "pf2-comparator.json"), theorems, STANDARD_AXIOMS)
                cmd = comparator_command(root, config, challenge, export, sandbox=False)
            else:
                cmd = kernel_command(root, kernel, export, job)
            key = f"{options.mode}/{kernel}/{attempt + 1}"
            print("---", key, flush=True)
            if options.mode.startswith("job") or options.mode.startswith("appcontainer"):
                row = windows_run(options.sandbox, options.mode, cmd, root, job, options.cap_min, options.memory_gib,
                                  children=kernel == "comparator")
                summary = {k: row.get(k) for k in ("ending", "exit_code", "wall_seconds", "peak_job_memory", "grant_seconds")}
            else:
                row = run_tree(wrap(options.mode, cmd, root, job), env=env, cwd=job, timeout=options.cap_min * 60,
                               memory_gib=options.memory_gib or None, stack_unlimited=True)
                row.pop("stdout_tail", None)
                summary = {k: row.get(k) for k in ("ending", "exit", "wall_s", "tree_rss", "scope_anon")}
            record[key] = row
            print(json.dumps(summary), flush=True)
            write_json(options.out, record)
    return 0


def probe(options) -> int:
    """Linux and macOS: the checks the Windows prototype's `probe` makes, in the mode's sandbox."""
    job = os.path.abspath(options.job)
    outside = os.path.abspath(options.outside)
    os.makedirs(job, exist_ok=True)
    os.makedirs(outside, exist_ok=True)
    with open(os.path.join(job, "inside.txt"), "w", encoding="utf-8") as handle:
        handle.write("inside the job folder\n")
    secret = os.path.join(outside, "secret.txt")
    with open(secret, "w", encoding="utf-8") as handle:
        handle.write("outside the job folder\n")
    home_secret = os.path.join(os.path.expanduser("~"), "pf2-home-secret.txt")
    with open(home_secret, "w", encoding="utf-8") as handle:
        handle.write("in the user's home\n")
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(("127.0.0.1", 0))
    listener.listen(8)
    port = listener.getsockname()[1]
    accepted = [0]

    def serve() -> None:
        while True:
            try:
                conn, _ = listener.accept()
            except OSError:
                return
            accepted[0] += 1
            conn.sendall(b"HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok")
            conn.close()

    threading.Thread(target=serve, daemon=True).start()
    cat = shutil.which("cat") or "/bin/cat"
    sh = shutil.which("sh") or "/bin/sh"
    curl = shutil.which("curl") or "/usr/bin/curl"
    ls = shutil.which("ls") or "/bin/ls"
    cases = {
        "read-inside": [cat, os.path.join(job, "inside.txt")],
        "read-outside": [cat, secret],
        "read-home": [cat, home_secret],
        "read-etc-hosts": [cat, "/etc/hosts"],
        "write-outside": [sh, "-c", f"echo x > {os.path.join(outside, 'written.txt')}"],
        "list-processes": [ls, "/proc"] if sys.platform.startswith("linux") else [sh, "-c", "ps -A | wc -l"],
        "connect-address": [curl, "-sS", "-m", "10", "-o", "/dev/null", "http://1.1.1.1/"],
        "connect-name": [curl, "-sS", "-m", "10", "-o", "/dev/null", "https://example.com/"],
        "connect-loopback": [curl, "-sS", "-m", "5", "-o", "/dev/null", f"http://127.0.0.1:{port}/"],
    }
    record = {"mode": options.mode, "variant": os.environ.get("PF2_SB_VARIANT", "")}
    for name, cmd in cases.items():
        written = os.path.join(outside, "written.txt")
        if os.path.exists(written):
            os.remove(written)
        before = accepted[0]
        argv = wrap(options.mode, cmd, os.path.abspath(options.root) if options.root else "/nonexistent", job, probe=True)
        try:
            done = subprocess.run(argv, capture_output=True, text=True, timeout=60)
            out = (done.stdout + done.stderr).strip()
            code = done.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            out, code = str(error), None
        if name == "list-processes" and code == 0 and sys.platform.startswith("linux"):
            out = f"{sum(1 for p in out.split() if p.isdigit())} processes visible"
        record[name] = {"exit": code, "says": out[-300:], "loopback_connections": accepted[0] - before,
                        "outside_file_written": os.path.exists(written)}
        print(name, json.dumps(record[name]), flush=True)
    listener.close()
    os.remove(home_secret)
    write_json(options.out, record)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("kernels")
    p.add_argument("--mode", required=True)
    p.add_argument("--root", required=True)
    p.add_argument("--job", required=True)
    p.add_argument("--theorems", required=True)
    p.add_argument("--kernels", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--sandbox", default="")
    p.add_argument("--repeat", type=int, default=1)
    p.add_argument("--memory-gib", type=float, default=0)
    p.add_argument("--cap-min", type=float, default=30)
    p = sub.add_parser("probe")
    p.add_argument("--mode", required=True)
    p.add_argument("--root", default="")
    p.add_argument("--job", required=True)
    p.add_argument("--outside", required=True)
    p.add_argument("--out", required=True)
    options = parser.parse_args()
    return kernels(options) if options.command == "kernels" else probe(options)


if __name__ == "__main__":
    sys.exit(main())
