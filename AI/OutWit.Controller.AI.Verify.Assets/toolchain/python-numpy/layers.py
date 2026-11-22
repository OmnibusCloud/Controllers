"""The Python layers of the numpy runtime's build, made as the asset tool makes its layers.

  layers.py stage-stdlib <cpython-Lib> <sysconfigdata-dir> <dest>
      the standard library's sources, the same set as the catalog's python314.zip (the WASI
      build's install without tests and without the packages a WASI guest cannot use: idlelib,
      tkinter, turtledemo, curses, multiprocessing, venv, ensurepip), with the WASI build's
      _sysconfigdata module, ready for compileall
  layers.py stage-package <installed-package-dir> <dest>
      a package's Python sources without its test suites (numpy's tests are a third of its
      files and no task imports them); headers, Cython declarations, type stubs and static
      libraries are left out
  layers.py zip <dir> <zip>
      a stored zip of the compiled tree for zipimport: bytecode and data files, no sources,
      entries in byte order with fixed times and modes, so the same tree gives the same bytes
  layers.py asset <dir> <zip>
      the same, deflated: the data asset that carries a runtime folder to the nodes
  layers.py changes <toolchain-dir> <build-info>
      adds to the build's record what numpy was built with that is not numpy's own: the
      patches applied to its sources, each with the words it starts with, and the files of
      the toolchain compiled in or put in front of its sources, each with its SHA-256

The asset's bytes depend on the entries and on the compressor: zlib at its level 9, the one
Python's zipfile calls. zlib 1.2.12, 1.3 and 1.3.1 were seen to give the same bytes. A Python
that brings another compressor in zlib's place (zlib-ng) gives an archive that unpacks to the
same files and hashes differently; the job that holds an archive to its pinned hash then says
so, and the files inside are still held by their own pins.
"""

import hashlib
import os
import shutil
import sys
import zipfile

SKIPPED_STDLIB = {"test", "site-packages", "idlelib", "tkinter", "turtledemo", "curses", "multiprocessing",
                  "venv", "ensurepip"}

# What the build takes from the toolchain's folder besides the patches, by what it is there for.
ADAPTATIONS = [
    ("put in front of numpy's sources by the compiler's command line (wasi-numpy.cross.in):",
     ["npy_fenv_flags.h", "npy_random_legacy_rename.h"]),
    ("compiled and linked in beside numpy's modules:",
     ["wasinumpy.c", "cxa_stubs.c"]),
    ("the entry points of python.wasm, the start-up snapshot, in place of CPython's main:",
     ["pynp_snapshot.c"]),
]


def walk(root):
    for folder, dirs, files in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d != "__pycache__")
        for name in sorted(files):
            path = os.path.join(folder, name)
            yield os.path.relpath(path, root).replace(os.sep, "/"), path


def stage_stdlib(lib, sysconfig_dir, dest):
    count = 0
    for relative, path in walk(lib):
        if any(relative == s or relative.startswith(s + "/") for s in SKIPPED_STDLIB):
            continue
        copy(path, os.path.join(dest, relative))
        count += 1
    data = [n for n in sorted(os.listdir(sysconfig_dir)) if n.startswith("_sysconfigdata_") and n.endswith(".py")]
    if len(data) != 1:
        sys.exit(f"no single _sysconfigdata module in {sysconfig_dir}: {data}")
    for name in data:
        copy(os.path.join(sysconfig_dir, name), os.path.join(dest, name))
    print(f"stdlib: {count} files and {', '.join(data)}")


def stage_package(package, dest):
    kept = 0
    left = {}
    for relative, path in walk(package):
        parts = relative.split("/")
        if "tests" in parts[:-1]:
            left["tests"] = left.get("tests", 0) + 1
            continue
        if not relative.endswith(".py"):
            kind = os.path.splitext(relative)[1] or parts[-1]
            left[kind] = left.get(kind, 0) + 1
            continue
        copy(path, os.path.join(dest, relative))
        kept += 1
    print(f"{os.path.basename(package)}: {kept} modules; left out: " +
          ", ".join(f"{n} {k}" for k, n in sorted(left.items())))


def make_zip(root, target):
    entries = []
    for relative, path in walk(root):
        if relative.endswith(".py"):
            if not os.path.isfile(path + "c"):
                sys.exit(f"{relative} was not compiled")
            continue
        entries.append((relative, path))
    write_zip(entries, target, zipfile.ZIP_STORED)


def make_asset(root, target):
    write_zip(list(walk(root)), target, zipfile.ZIP_DEFLATED)


def write_zip(entries, target, compression):
    if os.path.exists(target):
        os.remove(target)
    level = 9 if compression == zipfile.ZIP_DEFLATED else None
    with zipfile.ZipFile(target, "w", compression) as z:
        for relative, path in entries:
            info = zipfile.ZipInfo(relative, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            with open(path, "rb") as f:
                z.writestr(info, f.read(), compress_type=compression, compresslevel=level)
    print(f"{os.path.basename(target)}: {len(entries)} entries, {os.path.getsize(target)} bytes")


def record_changes(toolchain, build_info):
    lines = ["",
             "numpy's sources are not built as they are. What was changed, each file with its SHA-256",
             "(the patches lie in patches/ beside this file; every file named here is in",
             "toolchain/python-numpy of the controller's repository):",
             "",
             "patches to numpy's sources, in the order they are applied:"]
    patches = os.path.join(toolchain, "patches")
    for name in sorted(n for n in os.listdir(patches) if n.endswith(".patch")):
        path = os.path.join(patches, name)
        lines.append(f"  {sha256(path)}  patches/{name}")
        with open(path, encoding="utf-8") as patch:
            for line in patch:
                # A patch starts with what it changes and where it is from, up to its first empty line.
                if not line.strip():
                    break
                lines.append("      " + line.rstrip())
    for title, names in ADAPTATIONS:
        lines.append(title)
        for name in names:
            lines.append(f"  {sha256(os.path.join(toolchain, name))}  {name}")
    lines.append("CPython's sources are built as they are.")
    with open(build_info, "ab") as record:
        record.write(("\n".join(lines) + "\n").encode("utf-8"))
    print(f"{os.path.basename(build_info)}: {len(lines)} lines about the changes")


def sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def copy(source, target):
    os.makedirs(os.path.dirname(target), exist_ok=True)
    shutil.copyfile(source, target)


if __name__ == "__main__":
    commands = {"stage-stdlib": (stage_stdlib, 3), "stage-package": (stage_package, 2), "zip": (make_zip, 2),
                "asset": (make_asset, 2), "changes": (record_changes, 2)}
    if len(sys.argv) < 2 or sys.argv[1] not in commands or len(sys.argv) != commands[sys.argv[1]][1] + 2:
        sys.exit(__doc__)
    commands[sys.argv[1]][0](*sys.argv[2:])
