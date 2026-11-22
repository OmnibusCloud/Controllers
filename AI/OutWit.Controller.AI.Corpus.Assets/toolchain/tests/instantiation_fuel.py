#!/usr/bin/env python3
"""How much fuel the command line takes from a run before the guest's first instruction.

    instantiation_fuel.py <wasmtime> <pdftext.wasm>

Runs the module with no arguments (it prints its usage and exits 64, a few thousand
instructions) and finds, by bisection, the least fuel under which it gets there: once with
linear memory initialised from a copy-on-write image (wasmtime's default where it can) and
once without. Measured on Windows, wasmtime 48.0.8 charges one unit of fuel per byte of the
module's data segments when it copies them into memory at instantiation; this shows what the
Linux command line does. Information for the sandbox's fuel accounting, not a test.
"""

import subprocess
import sys

LIMIT = 1 << 27


def least(wasmtime, module, extra):
    low, high = 1, LIMIT
    while high - low > max(1, low // 500):
        middle = (low + high) // 2
        result = subprocess.run([wasmtime, "run", "-W", "fuel=%d" % middle] + extra + [module],
                                stdin=subprocess.DEVNULL, capture_output=True)
        if b"all fuel consumed" in result.stderr:
            low = middle
        else:
            high = middle
    return high


def data_bytes(path):
    """The bytes of the module's data section (section 11)."""
    with open(path, "rb") as handle:
        data = handle.read()
    position = 8
    while position < len(data):
        section = data[position]
        size = shift = 0
        position += 1
        while True:
            byte = data[position]
            position += 1
            size |= (byte & 0x7F) << shift
            shift += 7
            if not byte & 0x80:
                break
        if section == 11:
            return size
        position += size
    return 0


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    print("data section: %d bytes" % data_bytes(argv[2]))
    for label, extra in (("copy-on-write image", ["-O", "memory-init-cow=y"]), ("segments copied", ["-O", "memory-init-cow=n"])):
        print("least fuel of a usage run, %s: %d" % (label, least(argv[1], argv[2], extra)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
