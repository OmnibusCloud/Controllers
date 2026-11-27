"""PF-1 spike: what the PF-1 scripts share (on top of PF-0's harness/pf0lib.py).

Pins, a Lean release fetched by version, the names an export must carry for `lake comparator`,
zstd sizes, a short reading of an export, and one kernel run measured with caps.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "harness"))

from pf0lib import (EXE, IS_LINUX, IS_MACOS, IS_WINDOWS, run_measured, sha256_file,  # noqa: E402,F401
                    tree_bytes, write_json)

STANDARD_AXIOMS = ["propext", "Quot.sound", "Classical.choice"]
# What `lake comparator` exports beside the theorems (Lake.Check: builtinTargets, primitiveTargets).
QUOT = ["Quot", "Quot.mk", "Quot.lift", "Quot.ind"]
PRIMITIVES = ["Nat.add", "Nat.sub", "Nat.mul", "Nat.pow", "Nat.gcd", "Nat.div", "Nat.mod", "Nat.beq",
              "Nat.ble", "Nat.land", "Nat.lor", "Nat.xor", "Nat.shiftLeft", "Nat.shiftRight",
              "String.ofList", "Char.ofNat", "List", "eagerReduce", "Nat", "String", "String.mk", "Char",
              "optParam", "autoParam", "semiOutParam", "outParam"]


def lower_priority() -> None:
    """Below normal (Windows) or nice 10 (elsewhere) for this process and every child it starts,
    when PF1_BELOW_NORMAL=1: a machine that is a node of a fleet keeps its cores for its client."""
    if os.environ.get("PF1_BELOW_NORMAL") != "1":
        return
    if IS_WINDOWS:
        import ctypes
        kernel32 = ctypes.windll.kernel32
        kernel32.SetPriorityClass(kernel32.GetCurrentProcess(), 0x4000)
    else:
        os.nice(10)


def export_targets(theorems: list, axioms: list | None = None) -> list:
    """The names an export is made with, as `lake comparator` makes its own."""
    return QUOT + list(theorems) + list(axioms or STANDARD_AXIOMS) + PRIMITIVES


def load_pins(path: str | None = None) -> dict:
    pins = {}
    with open(path or os.path.join(HERE, "pins.env"), encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                pins[key.strip()] = value.strip()
    return pins


def github_env(name: str, value: str) -> None:
    path = os.environ.get("GITHUB_ENV")
    if path:
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(f"{name}={value}\n")


def unpack_tar_zst(archive: str, dest: str) -> str:
    os.makedirs(dest, exist_ok=True)
    if sys.version_info >= (3, 14):
        import tarfile
        with tarfile.open(archive, "r:zst") as tar:
            tar.extractall(dest, filter="fully_trusted")
        return "python tarfile"
    zstd = subprocess.Popen(["zstd", "-dc", archive], stdout=subprocess.PIPE)
    tar = subprocess.run(["tar", "-xf", "-", "-C", dest], stdin=zstd.stdout)
    zstd.stdout.close()
    if zstd.wait() != 0 or tar.returncode != 0:
        raise RuntimeError("zstd | tar failed")
    return "zstd | tar"


def fetch_lean(version: str, rid: str, dest: str) -> dict:
    """The official release of `version` for `rid`, checked against its pinned digest, unpacked
    under `dest`. Returns a record with the toolchain's root."""
    pins = load_pins()
    asset, pinned = pins[f"LEAN__{version}__{rid}"].split()
    url = f"https://github.com/leanprover/lean4/releases/download/v{version}/{asset}"
    os.makedirs(dest, exist_ok=True)
    archive = os.path.join(dest, asset)
    record = {"version": version, "rid": rid, "asset": asset, "pinned_sha256": pinned}
    started = time.monotonic()
    subprocess.run(["curl", "-fsSL", "--retry", "3", "--max-time", "1800", "-o", archive, url], check=True)
    record["download_s"] = round(time.monotonic() - started, 1)
    record["bytes"] = os.path.getsize(archive)
    record["sha256"] = sha256_file(archive)
    if pinned != "-" and record["sha256"] != pinned:
        os.remove(archive)
        raise SystemExit(f"HASH MISMATCH {asset}: pinned {pinned}, got {record['sha256']}")
    record["checked"] = pinned != "-"
    started = time.monotonic()
    folder = os.path.join(dest, f"lean-{version}")
    unpack_tar_zst(archive, folder)
    record["unpack_s"] = round(time.monotonic() - started, 1)
    os.remove(archive)
    tops = os.listdir(folder)
    root = os.path.join(folder, tops[0]) if len(tops) == 1 else folder
    record["root"] = root.replace("\\", "/")
    record["unpacked_bytes"] = tree_bytes(root)[0]
    return record


def tool(root: str, name: str) -> str:
    return os.path.join(root, "bin", name + EXE)


def lean_env(root: str, lean_path: list | None = None, extra: dict | None = None) -> dict:
    env = dict(os.environ)
    env["PATH"] = os.path.join(root, "bin") + os.pathsep + env.get("PATH", "")
    if lean_path is not None:
        env["LEAN_PATH"] = os.pathsep.join(lean_path)
    env["LEAN_ABORT_ON_PANIC"] = "1"
    for key in ("ELAN_TOOLCHAIN", "LEAN_SYSROOT"):
        env.pop(key, None)
    if extra:
        env.update(extra)
    return env


def zstd_module():
    try:
        from compression import zstd
        return zstd
    except ImportError:
        return None


def compress(source: str, target: str, level: int = 3) -> dict:
    zstd = zstd_module()
    started = time.monotonic()
    if zstd is not None:
        with open(source, "rb") as reader, zstd.open(target, "wb", level=level) as writer:
            shutil.copyfileobj(reader, writer, 1 << 22)
    else:
        subprocess.run(["zstd", "-q", "-f", f"-{level}", "-T0", source, "-o", target], check=True)
    return {"level": level, "bytes": os.path.getsize(target), "s": round(time.monotonic() - started, 1)}


def decompress(source: str, target: str) -> float:
    zstd = zstd_module()
    started = time.monotonic()
    if zstd is not None:
        with zstd.open(source, "rb") as reader, open(target, "wb") as writer:
            shutil.copyfileobj(reader, writer, 1 << 22)
    else:
        subprocess.run(["zstd", "-q", "-d", "-f", source, "-o", target], check=True)
    return round(time.monotonic() - started, 1)


def read_export_summary(path: str) -> dict:
    """The meta line, the lines by kind and the declarations by kind of an NDJSON export."""
    kinds = {}
    decls = {}
    lines = 0
    meta = None
    longest = 0
    with open(path, "rb") as handle:
        first = handle.readline()
        try:
            meta = json.loads(first).get("meta")
        except ValueError:
            meta = None
        lines = 1
        for raw in handle:
            lines += 1
            longest = max(longest, len(raw))
            head = raw[:12]
            if head.startswith(b'{"in"'):
                kinds["name"] = kinds.get("name", 0) + 1
            elif head.startswith(b'{"il"'):
                kinds["level"] = kinds.get("level", 0) + 1
            elif head.startswith(b'{"ie"'):
                kinds["expr"] = kinds.get("expr", 0) + 1
            else:
                key = raw[2:raw.index(b'"', 2)].decode("ascii", "replace")
                decls[key] = decls.get(key, 0) + 1
    return {"meta": meta, "lines": lines, "line_kinds": kinds, "declaration_lines": decls,
            "longest_line": longest, "bytes": os.path.getsize(path), "sha256": sha256_file(path)}


def capped(cmd: list, env: dict | None = None, cwd: str | None = None, timeout: float = 3600,
           memory_gib: float | None = None, stdout_path: str | None = None, keep: int = 1 << 16) -> dict:
    """One process to its end, its wall and CPU time and peak memory recorded. A memory cap is a
    cgroup limit on Linux when `systemd-run` can set one (a real limit), and elsewhere a kill when
    the resident set is seen above it (sampled every 0.1 s)."""
    cap_bytes = int(memory_gib * (1 << 30)) if memory_gib else None
    how = None
    if cap_bytes and IS_LINUX and shutil.which("systemd-run") and os.environ.get("PF1_CGROUP") == "1":
        uid, gid = os.getuid(), os.getgid()
        wrapper = ["sudo", "-n", "systemd-run", "--quiet", "--scope", "-p", f"MemoryMax={cap_bytes}",
                   "-p", "MemorySwapMax=0", f"--uid={uid}", f"--gid={gid}", "--setenv=HOME=" + os.environ.get("HOME", "/tmp")]
        if env:
            for key in ("PATH", "LEAN_PATH", "LEAN_ABORT_ON_PANIC"):
                if key in env:
                    wrapper.append(f"--setenv={key}={env[key]}")
        cmd = wrapper + ["--"] + cmd
        how = "cgroup"
        cap_for_sampling = None
    else:
        cap_for_sampling = cap_bytes
        how = "sampled" if cap_bytes else None
    row = run_measured(cmd, env=env, cwd=cwd, timeout=timeout, memory_cap=cap_for_sampling,
                       stdout_path=stdout_path, keep=keep)
    out = {"exit": row.get("exit"), "wall_s": row.get("wall_s"), "user_s": row.get("user_s"),
           "sys_s": row.get("sys_s"), "peak_rss": row.get("peak_rss"), "peak_private": row.get("peak_private"),
           "killed": row.get("killed"), "cap": how, "cap_gib": memory_gib}
    text = ((row.get("stdout") or "") + "\n" + (row.get("stderr") or "")).strip()
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    out["says"] = " / ".join(lines[-6:])[-600:]
    out["first"] = (lines[0][:300] if lines else "")
    out["json"] = None
    for line in reversed(lines):
        if line.startswith("{"):
            try:
                out["json"] = json.loads(line)
            except ValueError:
                pass
            break
    if row.get("error"):
        out["error"] = row["error"]
    return out


def nanoda_config(folder: str, export_path: str, axioms: list | None = None, threads: int = 4) -> str:
    """The configuration `lake comparator` writes for nanoda (Lake.Check.runExternalKernel)."""
    path = os.path.join(folder, f"nanoda-{threads}.json")
    write_json(path, {"use_stdin": False, "export_file_path": os.path.abspath(export_path),
                      "permitted_axioms": axioms or STANDARD_AXIOMS, "unpermitted_axiom_hard_error": False,
                      "num_threads": threads, "nat_extension": True, "string_extension": True})
    return path


def kernel_command(root: str, kernel: str, export_path: str, folder: str) -> list:
    """The command of one checker over an export, as `lake comparator` starts it."""
    if kernel == "leanchecker":
        return [tool(root, "leanchecker"), "--silent", "--from-export", export_path]
    if kernel == "leanchecker-paranoid":
        return [tool(root, "leanchecker-paranoid"), "--silent", "--from-export", export_path]
    if kernel.startswith("nanoda"):
        threads = int(kernel.split("-")[1]) if "-" in kernel else 4
        return [tool(root, "nanoda_bin"), nanoda_config(folder, export_path, threads=threads)]
    if kernel == "lean4lean":
        return [tool(root, "lean4lean"), "--import", export_path]
    if kernel in ("con-ron", "con-leche"):
        return [tool(root, kernel), export_path]
    raise ValueError(kernel)


def comparator_command(root: str, config: str, challenge: str, solution: str, sandbox: bool,
                       paranoid: bool = False) -> list:
    cmd = [tool(root, "lake"), "comparator", "--config", config, "--challenge-from-export", challenge,
           "--solution-from-export", solution]
    if not sandbox:
        cmd.append("--inadvisably-no-sandbox")
    if paranoid:
        cmd.append("--paranoid")
    return cmd


def comparator_config(path: str, theorems: list, axioms: list | None = None, nanoda: bool = False,
                      challenge_module: str = "Challenge", solution_module: str = "Solution") -> str:
    config = {"challenge_module": challenge_module, "solution_module": solution_module,
              "theorem_names": list(theorems), "permitted_axioms": list(axioms or STANDARD_AXIOMS)}
    if nanoda:
        config["enable_nanoda"] = True
    write_json(path, config)
    return path
