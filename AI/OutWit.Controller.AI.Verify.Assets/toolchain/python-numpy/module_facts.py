"""What a module is made of: its sections and their sizes, imports, exports and memory.

Usage: module_facts.py <module.wasm>

Exits 1 when the module is not one the sandbox runs as it is: an import outside WASI preview 1,
an exception tag section (13), a second memory or a shared one.
"""

import sys

NAMES = {0: "custom", 1: "type", 2: "import", 3: "function", 4: "table", 5: "memory", 6: "global",
         7: "export", 8: "start", 9: "element", 10: "code", 11: "data", 12: "datacount", 13: "tag"}


def leb(data, position):
    result = shift = 0
    while True:
        byte = data[position]
        position += 1
        result |= (byte & 0x7F) << shift
        shift += 7
        if not byte & 0x80:
            return result, position


def name(data, position):
    length, position = leb(data, position)
    return data[position:position + length].decode(), position + length


def limits(data, position):
    flags, position = leb(data, position)
    low, position = leb(data, position)
    high = None
    if flags & 1:
        high, position = leb(data, position)
    return flags, low, high, position


def main(path):
    data = open(path, "rb").read()
    position = 8
    sections = []
    imports = []
    exports = []
    memories = []
    while position < len(data):
        section = data[position]
        size, position = leb(data, position + 1)
        label = NAMES.get(section, str(section))
        if section == 0:
            label += ":" + name(data, position)[0]
        sections.append((label, size))
        cursor = position
        if section == 2:
            count, cursor = leb(data, cursor)
            for _ in range(count):
                module, cursor = name(data, cursor)
                field, cursor = name(data, cursor)
                kind = data[cursor]
                cursor += 1
                if kind == 0:
                    _, cursor = leb(data, cursor)
                elif kind == 1:
                    cursor += 1
                    _, _, _, cursor = limits(data, cursor)
                elif kind == 2:
                    flags, low, high, cursor = limits(data, cursor)
                    memories.append(("imported", flags, low, high))
                elif kind == 3:
                    cursor += 2
                elif kind == 4:
                    cursor += 1
                    _, cursor = leb(data, cursor)
                imports.append(f"{module}.{field}")
        elif section == 5:
            count, cursor = leb(data, cursor)
            for _ in range(count):
                flags, low, high, cursor = limits(data, cursor)
                memories.append(("defined", flags, low, high))
        elif section == 7:
            count, cursor = leb(data, cursor)
            for _ in range(count):
                field, cursor = name(data, cursor)
                kind = data[cursor]
                _, cursor = leb(data, cursor + 1)
                exports.append(f"{field}({'func table memory global tag'.split()[kind]})")
        position += size

    print(f"{path.split('/')[-1]}: {len(data):,} bytes")
    for label, size in sections:
        print(f"  {label:<28}{size:>14,}")
    print(f"  imports ({len(imports)}): {', '.join(imports)}")
    print(f"  exports: {', '.join(exports)}")
    for origin, flags, low, high in memories:
        print(f"  memory ({origin}): {low} pages initial ({low * 64 // 1024} MiB), "
              f"max {high if high is not None else 'none'}, flags {flags}")

    problems = [i for i in imports if not i.startswith("wasi_snapshot_preview1.")]
    if any(label == "tag" for label, _ in sections):
        problems.append("an exception tag section")
    # flags: 2 shared, 4 64-bit addresses
    if len(memories) != 1 or memories[0][1] & 6:
        problems.append(f"memories {memories}")
    if problems:
        print("::error::not a module the sandbox runs as it is: " + "; ".join(problems))
        return 1
    print("  WASI preview 1 imports only, one 32-bit memory, no exception tag section")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1]))
