"""The Python layers of the numpy runtime's build, made as the asset tool makes its layers.

  layers.py stage-stdlib <cpython-Lib> <sysconfigdata-dir> <dest>
      the standard library's sources as `make install` lays them out with
      --disable-test-modules (no Lib/test, no idlelib/idle_test), with the WASI build's
      _sysconfigdata module, ready for compileall
  layers.py stage-package <installed-package-dir> <dest>
      a package's Python sources without its test suites (numpy's tests are a third of its
      files and no task imports them); headers, Cython declarations, type stubs and static
      libraries are left out
  layers.py zip <dir> <zip>
      a stored zip of the compiled tree for zipimport: bytecode and data files, no sources,
      entries in byte order with fixed times and modes, so the same tree gives the same bytes
"""

import os
import shutil
import sys
import zipfile

SKIPPED_STDLIB = {"test", "idlelib/idle_test", "site-packages"}


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
    data = [n for n in sorted(os.listdir(sysconfig_dir)) if n.startswith("_sysconfig")]
    if not any(n.startswith("_sysconfigdata_") and n.endswith(".py") for n in data):
        sys.exit(f"no _sysconfigdata module in {sysconfig_dir}")
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
    if os.path.exists(target):
        os.remove(target)
    with zipfile.ZipFile(target, "w", zipfile.ZIP_STORED) as z:
        for relative, path in entries:
            info = zipfile.ZipInfo(relative, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            with open(path, "rb") as f:
                z.writestr(info, f.read())
    print(f"{os.path.basename(target)}: {len(entries)} entries, {os.path.getsize(target)} bytes")


def copy(source, target):
    os.makedirs(os.path.dirname(target), exist_ok=True)
    shutil.copyfile(source, target)


if __name__ == "__main__":
    commands = {"stage-stdlib": (stage_stdlib, 3), "stage-package": (stage_package, 2), "zip": (make_zip, 2)}
    if len(sys.argv) < 2 or sys.argv[1] not in commands or len(sys.argv) != commands[sys.argv[1]][1] + 2:
        sys.exit(__doc__)
    commands[sys.argv[1]][0](*sys.argv[2:])
