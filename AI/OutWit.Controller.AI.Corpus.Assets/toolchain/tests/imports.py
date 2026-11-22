#!/usr/bin/env python3
"""What the module imports, checked against what the extractor may use.

    imports.py <pdftext.wasm>

The module may import WASI preview 1 calls for its arguments, its environment (which it
empties before the library runs), the files it reads and writes, random_get (the standard
library seeds its hash maps with it; the sandbox's linker answers it from a fixed seed), and
proc_exit. Nothing else: no clock (a clock_time_get or poll_oneoff import would mean code
that can see the time), no sockets, no other module, no exception tags. Prints the imports
and the sections; exits 1 when the module imports more or carries an exception tag section.
"""

import sys

ALLOWED = {
    "args_get", "args_sizes_get",
    "environ_get", "environ_sizes_get",
    "fd_close", "fd_fdstat_get", "fd_filestat_get", "fd_prestat_get", "fd_prestat_dir_name",
    "fd_read", "fd_write", "fd_seek", "path_open",
    "random_get",
    "proc_exit",
}
EXCEPTION_TAG_SECTION = 13


def leb(data, position):
    result = shift = 0
    while True:
        byte = data[position]
        position += 1
        result |= (byte & 0x7F) << shift
        shift += 7
        if not byte & 0x80:
            return result, position


def read_name(data, position):
    length, position = leb(data, position)
    return data[position:position + length].decode("utf-8"), position + length


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 64
    with open(argv[1], "rb") as handle:
        data = handle.read()
    if data[:8] != b"\0asm\x01\0\0\0":
        print("not a core WebAssembly module")
        return 1
    position = 8
    sections = []
    imports = []
    while position < len(data):
        section = data[position]
        size, position = leb(data, position + 1)
        sections.append(section)
        if section == 2:
            cursor = position
            count, cursor = leb(data, cursor)
            for _ in range(count):
                module, cursor = read_name(data, cursor)
                name, cursor = read_name(data, cursor)
                kind = data[cursor]
                cursor += 1
                if kind != 0:
                    imports.append((module, name, "kind %d" % kind))
                    break
                _, cursor = leb(data, cursor)
                imports.append((module, name, "func"))
        position += size
    print("%d bytes; sections %s" % (len(data), sections))
    print("imports: " + ", ".join("%s.%s" % (module, name) for module, name, _ in imports))
    foreign = ["%s.%s (%s)" % import_ for import_ in imports
               if import_[0] != "wasi_snapshot_preview1" or import_[1] not in ALLOWED or import_[2] != "func"]
    if foreign or EXCEPTION_TAG_SECTION in sections:
        print("not allowed: %s%s" % (", ".join(foreign), "; an exception tag section" if EXCEPTION_TAG_SECTION in sections else ""))
        return 1
    print("every import is one the extractor may use; no clock, no sockets, no exception tags")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
