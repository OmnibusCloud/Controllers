#!/usr/bin/env python3
"""The one edit the wasm32-wasip1 build of Z3 makes to a source file, applied to an unpacked
source tree.

    patch.py <z3 source folder> <SHA-256 of the original src/util/hwf.cpp>

The file must be the one the edit was written against (pins.env holds its SHA-256 as
Z3_HWF_SHA256), and the text to replace must occur in it exactly once. Anything else stops
the build, so a new Z3 release is read before it is patched. Prints what it changed.

The edit adds nine lines, and an edit that added another number would give another module:
where Z3 checks for code that must not be reached (UNREACHABLE, VERIFY) it writes the file's
name and the line's number into the module, and hwf.cpp has such lines below the edit.
"""

import hashlib
import sys

PATH = "src/util/hwf.cpp"
WHAT = "hardware floats: WebAssembly rounds to nearest only"
OLD = "#else\n#define SETRM(RM) fesetround(RM)\n#endif\n"
NEW = (
    "#elif defined(__wasi__)\n"
    "// wasm32-wasip1 (the OmnibusCloud build): WebAssembly floats round to nearest, and there is no\n"
    "// rounding mode to set (wasi-libc's fenv.h has FE_TONEAREST only). Asking for a directed\n"
    "// rounding (the hwf subpaving tactic) stops Z3 at UNREACHABLE (exit 114) rather than going on\n"
    "// with the wrong rounding.\n"
    "#define FE_UPWARD 1\n"
    "#define FE_DOWNWARD 2\n"
    "#define FE_TOWARDZERO 3\n"
    "#define SETRM(RM) do { if ((RM) != FE_TONEAREST) { UNREACHABLE(); } } while (0)\n"
    "#else\n#define SETRM(RM) fesetround(RM)\n#endif\n"
)


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    full = "%s/%s" % (argv[1], PATH)
    with open(full, "rb") as handle:
        original = handle.read()
    seen = hashlib.sha256(original).hexdigest()
    if seen != argv[2]:
        print("%s is not the pinned file (%s, pinned %s): read the change before moving the pin" % (PATH, seen, argv[2]))
        return 1
    text = original.decode("utf-8")
    count = text.count(OLD)
    if count != 1:
        print("%s: the text to replace occurs %d times (expected once): %s" % (PATH, count, WHAT))
        return 1
    with open(full, "wb") as handle:
        handle.write(text.replace(OLD, NEW).encode("utf-8"))
    print("  %s: edited (%s)" % (PATH, WHAT))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
