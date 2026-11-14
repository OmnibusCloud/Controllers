#!/usr/bin/env python3
"""What the Z3 module imports and which wasm features it needs.

    imports.py <z3.wasm>

Prints the sections with their sizes, every import, the memory limits, the exception tags and
whether the code uses the exception-handling instructions (try_table / throw / throw_ref).
Exits 1 when the module imports anything outside WASI preview 1, or a WASI call the sandbox's
linker does not answer deterministically (sockets).
"""

import sys

SECTION_NAMES = {
    0: "custom", 1: "type", 2: "import", 3: "function", 4: "table", 5: "memory", 6: "global",
    7: "export", 8: "start", 9: "element", 10: "code", 11: "data", 12: "datacount", 13: "tag",
}
FORBIDDEN = {"sock_accept", "sock_recv", "sock_send", "sock_shutdown"}


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


def limits(data, position):
    flags = data[position]
    position += 1
    minimum, position = leb(data, position)
    maximum = None
    if flags & 1:
        maximum, position = leb(data, position)
    return flags, minimum, maximum, position


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
    memory = None
    tags = 0
    code = b""
    while position < len(data):
        section = data[position]
        size, position = leb(data, position + 1)
        name = SECTION_NAMES.get(section, str(section))
        if section == 0:
            custom, _ = read_name(data, position)
            name = "custom:" + custom
        sections.append("%s %d" % (name, size))
        if section == 2:
            cursor = position
            count, cursor = leb(data, cursor)
            for _ in range(count):
                module, cursor = read_name(data, cursor)
                field, cursor = read_name(data, cursor)
                kind = data[cursor]
                cursor += 1
                imports.append((module, field, kind))
                if kind == 0:
                    _, cursor = leb(data, cursor)
                elif kind == 2:
                    _, _, _, cursor = limits(data, cursor)
                else:
                    break
        elif section == 5:
            count, cursor = leb(data, position)
            if count:
                flags, minimum, maximum, _ = limits(data, cursor)
                memory = (flags, minimum, maximum)
        elif section == 13:
            tags, _ = leb(data, position)
        elif section == 10:
            code = data[position:position + size]
        position += size
    print("%d bytes" % len(data))
    print("sections: " + ", ".join(sections))
    print("imports (%d): %s" % (len(imports), ", ".join("%s.%s" % (m, f) for m, f, _ in imports)))
    if memory:
        flags, minimum, maximum = memory
        print("memory: %d pages initial (%d MiB), maximum %s%s" % (
            minimum, minimum * 65536 // 1048576,
            "none" if maximum is None else "%d pages (%d MiB)" % (maximum, maximum * 65536 // 1048576),
            ", 64-bit" if flags & 4 else ""))
    # 0x1f = try_table, 0x08 = throw, 0x0a = throw_ref (exnref proposal); 0x06/0x07/0x18/0x19
    # are the legacy try/catch/delegate/catch_all. Byte counts are a hint (immediates can hold
    # the same bytes), the tag section is the proof that exceptions are used.
    print("exception tags: %d; opcode bytes 0x1f (try_table) %d, 0x0a (throw_ref) %d, 0x06 (legacy try) %d"
          % (tags, code.count(b"\x1f"), code.count(b"\x0a"), code.count(b"\x06")))
    foreign = ["%s.%s" % (m, f) for m, f, k in imports
               if m != "wasi_snapshot_preview1" or k != 0 or f in FORBIDDEN]
    if foreign:
        print("not allowed: " + ", ".join(foreign))
        return 1
    print("every import is a WASI preview 1 call")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
