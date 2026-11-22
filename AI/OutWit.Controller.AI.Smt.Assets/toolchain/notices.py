#!/usr/bin/env python3
"""The third-party notices that travel with the solver module of the Smt controller.

    notices.py <pins.env> <out.txt> [<download-dir>]

The module is Z3 with the C library, the C++ library and the compiler's routines of the
wasi-sdk linked into it. The file this writes lists them and gives the terms of each part.
No text is written from memory: Z3's licence is the file that lies beside the notices, and
every other text is a file of its project's repository at the commit the SDK was built from
(pins.env names the two commits, and build.sh holds the SDK to them), fetched and held to the
SHA-256 pins.env gives for it. A text without a pin is reported as UNPINNED with the hash
that was seen and nothing is written (exit 3); a text that does not hash to its pin stops
the run.

What is linked was read from the module's function names: wasi-libc with its allocator and
the parts of musl and cloudlibc in it, libc++, libc++abi, libunwind, and compiler-rt's
builtins. The output is a function of pins.env and of this file: LF line ends, every text
unchanged but for its line ends.

Run with python3 -I.
"""

import hashlib
import os
import re
import sys
import tempfile
import time
import urllib.request

RULE = "=" * 80
WIDTH = 96

# Where a text is: the project's repository at a commit, by the pins.env key of the commit.
RAW = {
    "WASI_LIBC_COMMIT": "https://raw.githubusercontent.com/WebAssembly/wasi-libc/%s/%s",
    "LLVM_COMMIT": "https://raw.githubusercontent.com/llvm/llvm-project/%s/%s",
}

LLVM_LICENCE = "Apache-2.0 WITH LLVM-exception, and the earlier terms of LLVM the text gives"

# The parts, in the order they are written. {module}, {z3}, {sdk}, {libc} and {llvm} are filled
# from pins.env (the module's file name, the versions, the first twelve digits of the commits).
# A text is (the pins.env key of the commit, the path in the repository, the pins.env key of
# the text's SHA-256); "source" asks for the line that names Z3's source archive and its pin.
PARTS = [
    {
        "name": "Z3",
        "version": "{z3}",
        "licence": "MIT",
        "in": "{module}, the solver",
        "source": True,
        "notes": [
            "Two files of that archive are changed for a guest that has no threads and no rounding modes: "
            "src/util/scoped_timer.cpp is replaced (a timer never fires) and src/util/hwf.cpp is edited (a "
            "rounding other than to nearest stops the solver). The changes are wasi/scoped_timer.cpp and patch.py "
            "of the toolchain.",
            "Individual source files of Z3 name further copyright holders in their headers and are distributed "
            "under the same licence; the source archive above, at the address and with the SHA-256 given, is the "
            "place to read them.",
        ],
        "files": ["LICENSE-z3.txt"],
    },
    {
        "name": "wasi-libc",
        "version": "as of wasi-sdk {sdk}, commit {libc}",
        "licence": "Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT",
        "in": "{module}, linked statically: the C library",
        "notes": [
            "dlmalloc, the allocator inside wasi-libc, is CC0, as the first text says: it asks for no notice and "
            "none is given.",
        ],
        "texts": [
            ("WASI_LIBC_COMMIT", "LICENSE", "WASI_LIBC_LICENSE_SHA256"),
            ("WASI_LIBC_COMMIT", "LICENSE-APACHE-LLVM", "WASI_LIBC_LICENSE_APACHE_LLVM_SHA256"),
            ("WASI_LIBC_COMMIT", "LICENSE-APACHE", "WASI_LIBC_LICENSE_APACHE_SHA256"),
            ("WASI_LIBC_COMMIT", "LICENSE-MIT", "WASI_LIBC_LICENSE_MIT_SHA256"),
        ],
    },
    {
        "name": "musl",
        "version": "the part of it in wasi-libc",
        "licence": "MIT",
        "in": "{module}, linked statically: most of the C library's functions",
        "texts": [("WASI_LIBC_COMMIT", "libc-top-half/musl/COPYRIGHT", "WASI_LIBC_MUSL_COPYRIGHT_SHA256")],
    },
    {
        "name": "cloudlibc",
        "version": "the part of it in wasi-libc",
        "licence": "BSD-2-Clause",
        "in": "{module}, linked statically: the C library's calls to the system",
        "texts": [("WASI_LIBC_COMMIT", "libc-bottom-half/cloudlibc/LICENSE", "WASI_LIBC_CLOUDLIBC_LICENSE_SHA256")],
    },
    {
        "name": "musl-fts",
        "version": "the part of it in wasi-libc",
        "licence": "BSD-3-Clause",
        "in": "the C library {module} is linked with: its walk of a file tree, of which a module that walks none "
              "has nothing",
        "texts": [("WASI_LIBC_COMMIT", "fts/musl-fts/COPYING", "WASI_LIBC_MUSL_FTS_COPYING_SHA256")],
    },
    {
        "name": "libc++",
        "version": "of the LLVM in wasi-sdk {sdk}, commit {llvm}",
        "licence": LLVM_LICENCE,
        "in": "{module}, linked statically: the C++ standard library",
        "texts": [("LLVM_COMMIT", "libcxx/LICENSE.TXT", "LLVM_LIBCXX_LICENSE_SHA256")],
    },
    {
        "name": "libc++abi",
        "version": "of the LLVM in wasi-sdk {sdk}, commit {llvm}",
        "licence": LLVM_LICENCE,
        "in": "{module}, linked statically: what the C++ library stands on (exceptions, type information)",
        "texts": [("LLVM_COMMIT", "libcxxabi/LICENSE.TXT", "LLVM_LIBCXXABI_LICENSE_SHA256")],
    },
    {
        "name": "libunwind",
        "version": "of the LLVM in wasi-sdk {sdk}, commit {llvm}",
        "licence": LLVM_LICENCE,
        "in": "{module}, linked statically: the unwinding of a C++ exception",
        "texts": [("LLVM_COMMIT", "libunwind/LICENSE.TXT", "LLVM_LIBUNWIND_LICENSE_SHA256")],
    },
    {
        "name": "compiler-rt",
        "version": "the builtins of the LLVM in wasi-sdk {sdk}, commit {llvm}",
        "licence": LLVM_LICENCE,
        "in": "{module}, linked statically: the routines the compiler calls for what the machine has no "
              "instruction for",
        "texts": [("LLVM_COMMIT", "compiler-rt/LICENSE.TXT", "LLVM_COMPILER_RT_LICENSE_SHA256")],
    },
]

TITLE = "{module}, the solver module of the OmnibusCloud Smt controller"

ABOUT = [
    "What this file is about: {module}, the command line of Z3 {z3} built for WASI (wasm32-wasip1) by the "
    "controller's toolchain with wasi-sdk {sdk}, from the source archive of Z3's release. The C library, the C++ "
    "library and the compiler's routines of that SDK are linked into it.",
    "This file is written by the toolchain when it packs the module (notices.py in "
    "AI/OutWit.Controller.AI.Smt.Assets/toolchain of the controller's repository, "
    "https://github.com/OmnibusCloud/Controllers). No text here is written from memory: each is a file at the "
    "address given with it, held to the SHA-256 given with it, or a file that lies beside this one. A text is "
    "unchanged but for its line ends.",
    "The controller itself is its authors' work under the MIT licence. This file is about what it carries of "
    "others.",
]


def read_pins(path):
    values = {}
    with open(path, encoding="utf-8") as lines:
        for line in lines:
            match = re.match(r"^([A-Z0-9_]+)=(.*)$", line.strip())
            if match:
                values[match.group(1)] = match.group(2)
    return values


def fetch(url, pin, cache):
    """The bytes at an address: from the download folder when they are there, else fetched."""
    path = os.path.join(cache, hashlib.sha256(url.encode("utf-8")).hexdigest()[:16] + "-" + re.sub("[^A-Za-z0-9._+-]", "_", url.rsplit("/", 1)[-1]))
    if pin and os.path.isfile(path):
        with open(path, "rb") as source:
            data = source.read()
        if hashlib.sha256(data).hexdigest() == pin:
            return data
    error = None
    for attempt in range(4):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "smt-toolchain-notices"})
            with urllib.request.urlopen(request, timeout=120) as response:
                data = response.read()
            break
        except OSError as failure:
            error = failure
            time.sleep(3 * (attempt + 1))
    else:
        sys.exit("DOWNLOAD FAILED %s: %s" % (url, error))
    with open(path, "wb") as target:
        target.write(data)
    return data


def plain(data):
    """A text as it is printed: its own characters, LF line ends, one line end at its end."""
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        text = data.decode("latin-1")
    text = text.lstrip(chr(0xFEFF)).replace("\r\n", "\n").replace("\r", "\n")
    return text.rstrip("\n") + "\n"


def wrapped(text, first="", rest=""):
    """A paragraph in lines of WIDTH at most, the first after one prefix and the others after another."""
    lines = []
    line = first
    fresh = True
    for word in text.split(" "):
        if fresh:
            line = line + word
            fresh = False
        elif len(line) + 1 + len(word) > WIDTH:
            lines.append(line)
            line = rest + word
        else:
            line = line + " " + word
    return "\n".join(lines + [line])


def title_of(part, words):
    version = part["version"].format(**words)
    return "%s %s" % (part["name"], version) if version[0].isdigit() else "%s (%s)" % (part["name"], version)


def main(arguments):
    if len(arguments) not in (2, 3):
        sys.exit(__doc__)
    pins = read_pins(arguments[0])
    cache = arguments[2] if len(arguments) == 3 else os.path.join(tempfile.gettempdir(), "smt-toolchain-notices")
    os.makedirs(cache, exist_ok=True)

    words = {
        "module": "z3-%s.wasm" % pins["Z3_VERSION"],
        "z3": pins["Z3_VERSION"],
        "sdk": pins["WASI_SDK_VERSION"],
        "libc": pins["WASI_LIBC_COMMIT"][:12],
        "llvm": pins["LLVM_COMMIT"][:12],
        "source": pins["Z3_SOURCE_URL"],
        "source_sha256": pins["Z3_SOURCE_SHA256"],
    }

    # Every text first: nothing is written while one is without a pin or is not the pinned file.
    texts = {}
    unpinned = 0
    for part in PARTS:
        for commit, path, key in part.get("texts", []):
            url = RAW[commit] % (pins[commit], path)
            pin = pins.get(key, "")
            data = fetch(url, pin, cache)
            seen = hashlib.sha256(data).hexdigest()
            if not pin:
                print("UNPINNED %s  %s (%s)" % (seen, url, key))
                unpinned += 1
            elif seen != pin:
                sys.exit("HASH MISMATCH %s: expected %s (%s), got %s" % (url, pin, key, seen))
            else:
                print("pinned   %s  %s" % (seen, url))
            texts[key] = (url, pin, plain(data))
    if unpinned:
        print("some licence texts are not pinned (see UNPINNED above): no notices were written")
        return 3

    out = []
    out.append("THIRD-PARTY NOTICES\n")
    out.append(TITLE.format(**words) + "\n\n")
    for paragraph in ABOUT:
        out.append(wrapped(paragraph.format(**words)) + "\n\n")

    out.append("Parts\n-----\n")
    for number, part in enumerate(PARTS, 1):
        out.append("%3d. %s: %s\n" % (number, title_of(part, words), part["licence"]))
    out.append("\n")

    for number, part in enumerate(PARTS, 1):
        out.append("%s\n%d. %s\n%s\n" % (RULE, number, title_of(part, words), RULE))
        out.append(wrapped(part["in"].format(**words), "In:       ", "          ") + "\n")
        out.append(wrapped(part["licence"], "Licence:  ", "          ") + "\n")
        if part.get("source"):
            out.append("Source:   %s\n          (SHA-256 of that archive: %s)\n" % (words["source"], words["source_sha256"]))
        for note in part.get("notes", []):
            out.append(wrapped(note.format(**words), "Note:     ", "          ") + "\n")
        for beside in part.get("files", []):
            out.append("Text:     %s, beside this file\n" % beside)
        keys = [key for _, _, key in part.get("texts", [])]
        for index, key in enumerate(keys, 1):
            url, pin, body = texts[key]
            label = "Text" if len(keys) == 1 else "Text %d of %d" % (index, len(keys))
            out.append("\n%s: %s\n(SHA-256 of that file: %s)\n\n%s" % (label, url, pin, body))
        out.append("\n")

    made = "".join(out).encode("utf-8")
    with open(arguments[1], "wb") as target:
        target.write(made)
    print("%s: %d parts, %d texts, %d bytes, SHA-256 %s" % (
        os.path.basename(arguments[1]), len(PARTS), len(texts), len(made), hashlib.sha256(made).hexdigest()))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
