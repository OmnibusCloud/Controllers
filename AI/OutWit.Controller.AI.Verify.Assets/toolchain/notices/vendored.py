"""The Rust crates a library is built from, read from a vendored source archive.

The engine publishes its sources with every crate it is built from beside them (vendor/) and
the lock file that names each crate's version. This module reads from such an archive the
lock file, every crate's manifest and every licence text (read), and walks the manifests
from one crate to the crates that are linked into it for one platform (resolve): a
dependency counts when it is of the kind that is linked (not a build script's, not a
procedural macro), when it is not optional or a feature that is on asks for it, and when
its target condition holds for the platform.

It follows manifests, not the compiler: features are the ones the manifests turn on from
the starting crate's defaults, and a crate the linker dropped is still found. Used by
notices.py; needs Python 3.11 (tomllib).
"""

import re
import sys
import tarfile
import tomllib

LICENCE_FILE = re.compile(r"(?i)^(licen[sc]e|copying|copyright|notice|unlicense)")


# The platforms the controller ships the engine library for, as a crate's manifest asks about them.
TARGETS = {
    "win-x64": {"triple": "x86_64-pc-windows-msvc", "target_os": "windows", "target_family": "windows",
                "target_arch": "x86_64", "target_env": "msvc", "target_vendor": "pc",
                "target_pointer_width": "64", "target_endian": "little"},
    "linux-x64": {"triple": "x86_64-unknown-linux-gnu", "target_os": "linux", "target_family": "unix",
                  "target_arch": "x86_64", "target_env": "gnu", "target_vendor": "unknown",
                  "target_pointer_width": "64", "target_endian": "little"},
    "osx-arm64": {"triple": "aarch64-apple-darwin", "target_os": "macos", "target_family": "unix",
                  "target_arch": "aarch64", "target_env": "", "target_vendor": "apple",
                  "target_pointer_width": "64", "target_endian": "little"},
}


def read(path):
    """The lock file, every manifest and every licence text of a vendored source archive."""
    files = {}
    with tarfile.open(path, "r:*") as archive:
        for entry in archive:
            if not entry.isfile():
                continue
            relative = entry.name.split("/", 1)[1] if "/" in entry.name else entry.name
            base = relative.rsplit("/", 1)[-1]
            if base in ("Cargo.toml", "Cargo.lock") or LICENCE_FILE.match(base):
                files[relative] = archive.extractfile(entry).read()

    root = tomllib.loads(files["Cargo.toml"].decode("utf-8"))
    workspace = root["workspace"]
    lock = {}
    names = {}
    for package in tomllib.loads(files["Cargo.lock"].decode("utf-8"))["package"]:
        lock[(package["name"], package["version"])] = package
        names.setdefault(package["name"], []).append(package["version"])

    manifests = {}
    for relative, data in sorted(files.items()):
        if not relative.endswith("Cargo.toml") or relative == "Cargo.toml":
            continue
        try:
            manifest = tomllib.loads(data.decode("utf-8"))
        except (tomllib.TOMLDecodeError, UnicodeDecodeError):
            continue
        package = manifest.get("package")
        if not package:
            continue
        version = package.get("version")
        if isinstance(version, dict):
            version = workspace["package"]["version"]
        key = (package["name"], version)
        folder = relative.rsplit("/", 1)[0]
        vendored = folder.startswith("vendor/")
        if key in lock and vendored == (lock[key].get("source") is not None) and (key not in manifests or len(folder) < len(manifests[key][1])):
            manifests[key] = (package, folder, manifest)

    texts = {}
    for relative, data in files.items():
        base = relative.rsplit("/", 1)[-1]
        if not relative.startswith("vendor/") or not LICENCE_FILE.match(base):
            continue
        parts = relative.split("/")
        texts.setdefault("/".join(parts[:2]), {})["/".join(parts[2:])] = data
    return manifests, lock, texts, workspace


def resolve(root, target, manifests, lock, workspace):
    """The crates linked into a library for one platform: a walk of the manifests with their features."""
    names = {}
    for key in lock:
        names.setdefault(key[0], []).append(key)
    inherited = workspace.get("dependencies", {})

    def locked(dependency):
        fields = dependency.split(" ")
        found = [key for key in names[fields[0]] if len(fields) == 1 or key[1] == fields[1]]
        if len(found) != 1:
            sys.exit(f"the lock file does not settle '{dependency}'")
        return found[0]

    def is_macro(key):
        library = manifests[key][2].get("lib", {})
        return bool(library.get("proc-macro") or library.get("proc_macro"))

    specs = {}

    def dependencies(key):
        if key in specs:
            return specs[key]
        manifest = manifests[key][2]
        tables = [manifest.get("dependencies", {})]
        for condition, table in manifest.get("target", {}).items():
            if holds(condition, target):
                tables.append(table.get("dependencies", {}))
        found = {}
        for table in tables:
            for name, spec in table.items():
                if isinstance(spec, str):
                    spec = {"version": spec}
                shared = {}
                if spec.get("workspace"):
                    shared = inherited.get(name, {})
                    if isinstance(shared, str):
                        shared = {"version": shared}
                entry = {
                    "package": spec.get("package", shared.get("package", name)),
                    "optional": bool(spec.get("optional", False)),
                    "default": spec.get("default-features", spec.get("default_features",
                               shared.get("default-features", shared.get("default_features", True)))),
                    "features": list(shared.get("features", [])) + list(spec.get("features", [])),
                }
                if name in found:
                    found[name]["optional"] = found[name]["optional"] and entry["optional"]
                    found[name]["default"] = found[name]["default"] or entry["default"]
                    found[name]["features"] += entry["features"]
                else:
                    found[name] = entry
        specs[key] = found
        return found

    def targets_of(key, name):
        package = dependencies(key)[name]["package"]
        return [locked(item) for item in lock[key].get("dependencies", []) if item.split(" ")[0] == package]

    enabled = {}
    active = {}
    pending = {}
    started = []
    work = []

    def start(key):
        if key in active:
            return
        if key not in manifests:
            sys.exit(f"no manifest for {key[0]} {key[1]} in the archive")
        active[key] = set()
        enabled[key] = set()
        started.append(key)
        if is_macro(key):
            return
        for name, spec in dependencies(key).items():
            if not spec["optional"]:
                activate(key, name)

    def activate(key, name):
        if name not in dependencies(key) or name in active[key]:
            return
        active[key].add(name)
        spec = dependencies(key)[name]
        for other in targets_of(key, name):
            start(other)
            wanted = list(spec["features"]) + (["default"] if spec["default"] else []) + pending.pop((key, name), [])
            work.extend((other, feature) for feature in wanted)

    def enable(key, feature):
        if is_macro(key) or feature in enabled[key]:
            return
        enabled[key].add(feature)
        table = manifests[key][2].get("features", {})
        if feature in table:
            for item in table[feature]:
                if item.startswith("dep:"):
                    activate(key, item[4:])
                elif "/" in item:
                    name, inner = item.split("/", 1)
                    if name.endswith("?"):
                        name = name[:-1]
                        if name in active[key]:
                            work.extend((other, inner) for other in targets_of(key, name))
                        else:
                            pending.setdefault((key, name), []).append(inner)
                    elif name in dependencies(key):
                        activate(key, name)
                        work.extend((other, inner) for other in targets_of(key, name))
                else:
                    work.append((key, item))
        elif feature in dependencies(key) and dependencies(key)[feature]["optional"]:
            activate(key, feature)

    first = locked(root)
    start(first)
    work.append((first, "default"))
    while work:
        key, feature = work.pop()
        enable(key, feature)
    return [key for key in started if not is_macro(key)]


def holds(condition, target):
    """Whether a manifest's target condition is true of a platform."""
    if not condition.startswith("cfg("):
        return condition == target["triple"]
    tokens = re.findall(r'[A-Za-z_][A-Za-z0-9_]*|"[^"]*"|[(),=]', condition[4:-1])

    def parse(at):
        token = tokens[at]
        if token in ("all", "any", "not") and at + 1 < len(tokens) and tokens[at + 1] == "(":
            values = []
            at += 2
            while tokens[at] != ")":
                value, at = parse(at)
                values.append(value)
                if tokens[at] == ",":
                    at += 1
            if token == "all":
                return all(values), at + 1
            if token == "any":
                return any(values), at + 1
            return not values[0], at + 1
        if at + 1 < len(tokens) and tokens[at + 1] == "=":
            return target.get(token) == tokens[at + 2].strip('"'), at + 3
        # A bare name: the family, or a build setting nobody turns on here (miri, loom, ...).
        return token in ("unix", "windows") and target["target_family"] == token, at + 1

    return parse(0)[0]
