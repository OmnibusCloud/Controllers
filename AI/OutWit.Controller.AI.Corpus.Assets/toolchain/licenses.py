#!/usr/bin/env python3
"""The licence notice that travels with pdftext.wasm: every crate compiled into the module,
with its licence expression and the licence files it carries, and the Rust standard library.

    licenses.py <cargo-metadata.json> <build-messages.jsonl> <target> <rust-license-dir> <out.txt>

<cargo-metadata.json> is `cargo metadata --format-version 1` of the crate, <build-messages.jsonl>
the `cargo build --message-format=json` output of the module's build, <target> its target
triple, <rust-license-dir> the folder with the COPYRIGHT and LICENSE-* files of the Rust
release. A crate counts when the build compiled a library of it for the target: what the
module links. Proc macros, build scripts and the libraries only they use run on the build
machine and do not ship. A text that several crates carry word for word is written once.
The output is a function of the inputs (sorted, LF).
"""

import hashlib
import json
import os
import sys

LICENSE_PREFIXES = ("LICENSE", "LICENCE", "COPYING", "COPYRIGHT", "NOTICE", "UNLICENSE")


def shipped_packages(metadata, messages_path, target):
    packages = {package["id"]: package for package in metadata["packages"]}
    root = metadata["resolve"]["root"]
    marker = "/%s/" % target
    linked = set()
    with open(messages_path, encoding="utf-8") as handle:
        for line in handle:
            if not line.startswith("{"):
                continue
            message = json.loads(line)
            if message.get("reason") != "compiler-artifact" or message["package_id"] == root:
                continue
            if "lib" in message["target"]["kind"] or "rlib" in message["target"]["kind"]:
                if any(marker in name.replace("\\", "/") for name in message["filenames"]):
                    linked.add(message["package_id"])
    return packages[root], sorted((packages[identifier] for identifier in linked), key=lambda p: (p["name"], p["version"]))


def license_files(folder):
    names = [name for name in os.listdir(folder) if name.upper().startswith(LICENSE_PREFIXES) and os.path.isfile(os.path.join(folder, name))]
    return sorted(names)


def read_text(path):
    with open(path, "rb") as handle:
        return handle.read().decode("utf-8", "replace").replace("\r\n", "\n").rstrip("\n") + "\n"


def main(argv):
    if len(argv) != 6:
        print(__doc__, file=sys.stderr)
        return 64
    with open(argv[1], encoding="utf-8") as handle:
        metadata = json.load(handle)
    root, packages = shipped_packages(metadata, argv[2], argv[3])
    if not packages:
        print("no library compiled for %s in %s" % (argv[3], argv[2]), file=sys.stderr)
        return 1

    out = []
    written = {}

    def section(title, folder, names):
        out.append("=" * 78 + "\n" + title + "\n" + "=" * 78 + "\n")
        if not names:
            out.append("(the package carries no licence file)\n\n")
        for name in names:
            text = read_text(os.path.join(folder, name))
            digest = hashlib.sha256(text.encode("utf-8")).hexdigest()
            if digest in written:
                out.append("-- %s: the same text as %s above\n\n" % (name, written[digest]))
                continue
            written[digest] = "%s, %s" % (title.split(" (")[0], name)
            out.append("-- %s\n\n%s\n" % (name, text))

    out.append("%s %s (%s): the PDF extractor of the Corpus controller, a WebAssembly module "
               "(wasm32-wasip1) in which the Rust standard library and the %d crates below are "
               "statically linked. Their licences follow.\n\n" % (root["name"], root["version"], root.get("license") or "", len(packages)))
    out.append("The crates, as Cargo.lock pins them:\n\n")
    for package in packages:
        out.append("  %s %s (%s)\n" % (package["name"], package["version"], package.get("license") or package.get("license_file") or "no licence expression"))
    out.append("\n")

    rust_folder = argv[4]
    section("Rust standard library (MIT OR Apache-2.0), with wasi-libc of the wasm32-wasip1 target "
            "(Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT, https://github.com/WebAssembly/wasi-libc)",
            rust_folder, license_files(rust_folder))
    for package in packages:
        folder = os.path.dirname(package["manifest_path"])
        section("%s %s (%s)" % (package["name"], package["version"], package.get("license") or "see the files"), folder, license_files(folder))

    with open(argv[5], "w", encoding="utf-8", newline="\n") as handle:
        handle.write("".join(out))
    print("%s: %d crates, %d distinct licence texts" % (os.path.basename(argv[5]), len(packages), len(written)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
