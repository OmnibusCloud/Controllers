"""Load-command closure of Mach-O binaries (author-side helper for trimming the macOS ParaView bundle).

    python macho_closure.py <libraries-dir> <root1> [<root2> ...]

The macOS twin of pe_closure.py / elf_closure.py: walks LC_LOAD_DYLIB / LC_LOAD_WEAK_DYLIB /
LC_REEXPORT_DYLIB starting from the roots (Contents/bin/pvpython and the Python extension modules),
resolves each referenced install name by basename against <libraries-dir> and prints the libraries
that are NOT reachable. dlopen'd modules (OSPRay devices, ParaView plugins) are not seen.
"""

import os
import struct
import sys

LOAD_COMMANDS = {0xC, 0x80000018, 0x8000001F}  # LC_LOAD_DYLIB, LC_LOAD_WEAK_DYLIB, LC_REEXPORT_DYLIB


def loads_of(data):
    """Basenames of the dylibs a Mach-O image (first slice of a fat file) loads."""
    if len(data) < 32:
        return []
    magic = struct.unpack_from(">I", data, 0)[0]
    if magic == 0xCAFEBABE:
        nfat = struct.unpack_from(">I", data, 4)[0]
        if nfat == 0:
            return []
        _, _, offset, size, _ = struct.unpack_from(">IIIII", data, 8)
        return loads_of(data[offset:offset + size])
    little = struct.unpack_from("<I", data, 0)[0]
    if little == 0xFEEDFACF:
        endian, header = "<", 32
    elif little == 0xFEEDFACE:
        endian, header = "<", 28
    elif magic == 0xFEEDFACF:
        endian, header = ">", 32
    elif magic == 0xFEEDFACE:
        endian, header = ">", 28
    else:
        return []
    ncmds = struct.unpack_from(endian + "I", data, 16)[0]
    names = []
    pos = header
    for _ in range(ncmds):
        if pos + 8 > len(data):
            break
        cmd, cmdsize = struct.unpack_from(endian + "II", data, pos)
        if cmdsize < 8:
            break
        if cmd in LOAD_COMMANDS:
            name_off = struct.unpack_from(endian + "I", data, pos + 8)[0]
            start = pos + name_off
            end = data.find(b"\0", start, pos + cmdsize)
            name = data[start:end if end > 0 else pos + cmdsize].decode("utf-8", "replace")
            names.append(os.path.basename(name))
        pos += cmdsize
    return names


def loads_of_file(path):
    try:
        with open(path, "rb") as handle:
            return loads_of(handle.read())
    except OSError:
        return []


def main(argv):
    lib_dir = os.path.abspath(argv[0])
    roots = [os.path.abspath(r) for r in argv[1:]]
    available = {}
    for name in os.listdir(lib_dir):
        path = os.path.join(lib_dir, name)
        if os.path.isfile(path) and not os.path.islink(path) and ".dylib" in name:
            available[name] = path
    seen = set()
    queue = list(roots)
    while queue:
        current = queue.pop()
        for name in loads_of_file(current):
            if name in available and name not in seen:
                seen.add(name)
                queue.append(available[name])
    unreachable = sorted(k for k in available if k not in seen)
    print("closure: %d of %d libraries reachable from %d root(s)" % (len(seen), len(available), len(roots)))
    total = 0
    print("UNREACHABLE (%d):" % len(unreachable))
    for k in unreachable:
        size = os.path.getsize(available[k])
        total += size
        print("   %-70s %8.1f MB" % (k, size / 1e6))
    print("unreachable total: %.0f MB" % (total / 1e6))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
