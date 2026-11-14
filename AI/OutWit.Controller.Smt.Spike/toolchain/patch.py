#!/usr/bin/env python3
"""The source changes the wasm32-wasip1 build of Z3 needs, applied to an unpacked source tree.

    patch.py <z3 source folder>

Each change replaces one exact piece of text that must occur exactly once; anything else stops
the build (a new Z3 release is then reviewed, not patched blindly). Prints what it changed.
"""

import sys

PATCHES = [
    (
        "src/util/hwf.cpp",
        "hardware floats: WebAssembly rounds to nearest only",
        "#else\n#define SETRM(RM) fesetround(RM)\n#endif\n",
        "#elif defined(__wasi__)\n"
        "// wasm32-wasip1 (OmnibusCloud SM-0): WebAssembly floats round to nearest, and there is no\n"
        "// rounding mode to set (wasi-libc's fenv.h has FE_TONEAREST only). Asking for a directed\n"
        "// rounding (the hwf subpaving tactic) stops Z3 at UNREACHABLE (exit 114) rather than going on\n"
        "// with the wrong rounding.\n"
        "#define FE_UPWARD 1\n"
        "#define FE_DOWNWARD 2\n"
        "#define FE_TOWARDZERO 3\n"
        "#define SETRM(RM) do { if ((RM) != FE_TONEAREST) { UNREACHABLE(); } } while (0)\n"
        "#else\n#define SETRM(RM) fesetround(RM)\n#endif\n",
    ),
]


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 64
    root = argv[1]
    for path, what, old, new in PATCHES:
        full = "%s/%s" % (root, path)
        with open(full, encoding="utf-8", newline="") as handle:
            text = handle.read()
        count = text.count(old)
        if count != 1:
            print("%s: the text to replace occurs %d times (expected once): %s" % (path, count, what))
            return 1
        with open(full, "w", encoding="utf-8", newline="") as handle:
            handle.write(text.replace(old, new))
        print("  %s: %s" % (path, what))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
