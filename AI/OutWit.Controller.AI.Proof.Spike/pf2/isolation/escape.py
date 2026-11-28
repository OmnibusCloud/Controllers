"""PF-2 spike, escape probes (follow-up of 2026-11-28): the probe (`probe/`, Native AOT) run as the
"kernel" outside any sandbox (the control) and inside each sandbox of this OS; for every check,
allowed, blocked or absent. The probe never prints what it reads, only counts and sizes.

    escape.py --probe <pf2probe[.exe]> --modes raw,bwrap,... --work <dir> --out <record.json>
              [--sandbox <Pf2Sandbox.exe>] [--label L]

The host side: a folder beside the job folder with a file in it, a file in the user's home, a TCP
listener on 127.0.0.1, a Unix socket in the folder beside the job folder (Linux, macOS) or a named
pipe (Windows), this script's own process id as the "host process", the job's GITHUB_ENV file, and
the runner's own files the workflow found (PF2_RUNNER_FILES, separated by ';').
Modes: raw; linux: bwrap, bwrap-host-ro; macos: sandbox-exec; windows: job, appcontainer,
appcontainer-nochildren.
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
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))

import isolation  # noqa: E402

IS_WINDOWS = sys.platform == "win32"


def serve_tcp() -> tuple[socket.socket, list]:
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(("127.0.0.1", 0))
    listener.listen(16)
    seen = [0]

    def loop() -> None:
        while True:
            try:
                conn, _ = listener.accept()
            except OSError:
                return
            seen[0] += 1
            conn.close()

    threading.Thread(target=loop, daemon=True).start()
    return listener, seen


def serve_unix(path: str) -> list:
    seen = [0]
    if IS_WINDOWS or not hasattr(socket, "AF_UNIX"):
        return seen
    if os.path.exists(path):
        os.remove(path)
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    server.bind(path)
    server.listen(16)

    def loop() -> None:
        while True:
            try:
                conn, _ = server.accept()
            except OSError:
                return
            seen[0] += 1
            conn.close()

    threading.Thread(target=loop, daemon=True).start()
    return seen


def serve_pipe(name: str) -> list:
    seen = [0]
    if not IS_WINDOWS:
        return seen
    from multiprocessing.connection import Listener
    listener = Listener(rf"\\.\pipe\{name}", family="AF_PIPE")

    def loop() -> None:
        while True:
            try:
                conn = listener.accept()
            except (OSError, EOFError):
                continue
            seen[0] += 1
            conn.close()

    threading.Thread(target=loop, daemon=True).start()
    return seen


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", required=True)
    parser.add_argument("--modes", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--sandbox", default="")
    parser.add_argument("--label", default="")
    options = parser.parse_args()
    work = os.path.realpath(os.path.abspath(options.work))
    probe_dir = os.path.join(work, "probe-bin")
    job = os.path.join(work, "probe-job")
    outside = os.path.join(work, "probe-outside")
    for folder in (probe_dir, job, outside):
        os.makedirs(folder, exist_ok=True)
    probe = os.path.join(probe_dir, os.path.basename(options.probe))
    shutil.copy2(options.probe, probe)
    if not IS_WINDOWS:
        os.chmod(probe, 0o755)
    with open(os.path.join(outside, "secret.txt"), "w", encoding="utf-8") as handle:
        handle.write("beside the job folder\n")
    home = os.path.expanduser("~")
    home_secret = os.path.join(home, "pf2-probe-home-secret.txt")
    with open(home_secret, "w", encoding="utf-8") as handle:
        handle.write("in the user's home\n")
    tcp, tcp_seen = serve_tcp()
    unix_path = os.path.join(outside, "pf2.sock")
    unix_seen = serve_unix(unix_path)
    pipe_seen = serve_pipe("pf2-probe-pipe")
    unix_sockets = [p for p in (unix_path, "/var/run/docker.sock", "/run/dbus/system_bus_socket",
                                "/var/run/mDNSResponder", "/var/run/syslog") if not IS_WINDOWS and os.path.exists(p)]
    args = ["--home", home, "--home-secret", home_secret, "--outside", outside,
            "--outside-secret", os.path.join(outside, "secret.txt"),
            "--loopback-port", str(tcp.getsockname()[1]), "--host-pid", str(os.getpid())]
    if os.environ.get("GITHUB_ENV"):
        args += ["--github-env", os.environ["GITHUB_ENV"]]
    if os.environ.get("PF2_RUNNER_FILES"):
        args += ["--runner-files", os.environ["PF2_RUNNER_FILES"]]
    if unix_sockets:
        args += ["--unix-socket", ";".join(unix_sockets)]
    if IS_WINDOWS:
        args += ["--pipe", "pf2-probe-pipe;docker_engine"]
        for name in ("APPDATA", "LOCALAPPDATA"):
            if os.environ.get(name):
                args += ["--" + name.lower(), os.environ[name]]
    record = json.load(open(options.out, encoding="utf-8")) if os.path.isfile(options.out) else {}
    for mode in [m for m in options.modes.split(",") if m]:
        out_file = os.path.join(job, f"probe-{mode}.json")
        if os.path.exists(out_file):
            os.remove(out_file)
        before = (tcp_seen[0], unix_seen[0], pipe_seen[0])
        cmd = [probe] + args + ["--out", out_file]
        if IS_WINDOWS and mode != "raw":
            argv = [options.sandbox, "run", "--name", "pf2-probe-kernel", "--job-dir", job, "--grant-read", probe_dir,
                    "--wall-s", "120", "--processes", "8", "--console", "detached"]
            if mode == "job":
                argv.append("--no-container")
            if mode == "appcontainer-nochildren":
                argv.append("--no-children")
            argv += ["--"] + cmd
        else:
            argv = isolation.wrap(mode, cmd, probe_dir, job)
        done = subprocess.run(argv, capture_output=True, text=True, timeout=300)
        row = {"exit": done.returncode, "launcher": (done.stdout + done.stderr)[-1500:]}
        try:
            with open(out_file, encoding="utf-8") as handle:
                row["probe"] = json.load(handle)
        except (OSError, ValueError) as error:
            row["probe"] = None
            row["error"] = str(error)
        row["host_saw"] = {"tcp": tcp_seen[0] - before[0], "unix": unix_seen[0] - before[1], "pipe": pipe_seen[0] - before[2]}
        record[mode] = row
        print(mode, "exit", done.returncode, "checks", len((row["probe"] or {}).get("checks", [])), row["host_saw"], flush=True)
        if row["probe"] is None:
            print(row["launcher"][-800:], flush=True)
    os.remove(home_secret)
    with open(options.out, "w", encoding="utf-8") as handle:
        json.dump(record, handle, indent=1)
    # The table: one row a check, one column a mode.
    modes = [m for m in record if record[m].get("probe")]
    names = []
    for m in modes:
        for c in record[m]["probe"]["checks"]:
            if c["name"] not in names:
                names.append(c["name"])
    print("| check | " + " | ".join(modes) + " |")
    for name in names:
        cells = []
        for m in modes:
            hit = [c for c in record[m]["probe"]["checks"] if c["name"] == name]
            cells.append(f"{hit[0]['result']}: {hit[0]['detail'][:60]}" if hit else "-")
        print(f"| {name} | " + " | ".join(cells) + " |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
