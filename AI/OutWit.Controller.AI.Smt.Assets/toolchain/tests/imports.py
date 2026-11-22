#!/usr/bin/env python3
"""What the solver module imports and carries, checked against what it was built to be.

    imports.py <z3.wasm> [<text> ...]

The module must import exactly the sixteen WASI preview 1 calls below, each a function, and
nothing else: its arguments and its environment, the clock (Z3 reads it for its statistics;
the sandbox answers from a clock that is pinned), the files it reads and writes, and
proc_exit. No random_get, no poll_oneoff, no sockets, no other module. A build that imports
one call more, or one less, has changed: the list is then read again, not widened.

It must carry one exception tag (C++ exceptions are WebAssembly exceptions of the standard
proposal, and one tag serves them all), no producers section (it would name the compiler's
own build), and none of the <text> arguments anywhere in its bytes: the folders of a build
machine, which the caller names.

Prints the sections, the imports, the memory limits and the features the module names;
exits 1 on a finding.
"""

import sys

EXPECTED = {
    "args_get", "args_sizes_get",
    "environ_get", "environ_sizes_get",
    "clock_time_get",
    "fd_close", "fd_fdstat_get", "fd_fdstat_set_flags", "fd_prestat_get", "fd_prestat_dir_name",
    "fd_read", "fd_seek", "fd_write",
    "path_filestat_get", "path_open",
    "proc_exit",
}
WASI = "wasi_snapshot_preview1"
SECTION_NAMES = {
    0: "custom", 1: "type", 2: "import", 3: "function", 4: "table", 5: "memory", 6: "global",
    7: "export", 8: "start", 9: "element", 10: "code", 11: "data", 12: "datacount", 13: "tag",
}
KINDS = {0: "func", 1: "table", 2: "memory", 3: "global", 4: "tag"}
PAGE = 65536


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
    flags, position = leb(data, position)
    minimum, position = leb(data, position)
    maximum = None
    if flags & 1:
        maximum, position = leb(data, position)
    return minimum, maximum, position


def read_imports(data, position):
    imports = []
    count, position = leb(data, position)
    for _ in range(count):
        module, position = read_name(data, position)
        name, position = read_name(data, position)
        kind = data[position]
        position += 1
        if kind == 0:
            _, position = leb(data, position)
        elif kind == 1:
            position += 1
            _, _, position = limits(data, position)
        elif kind == 2:
            _, _, position = limits(data, position)
        elif kind == 3:
            position += 2
        elif kind == 4:
            position += 1
            _, position = leb(data, position)
        else:
            raise ValueError("an import of an unknown kind %d" % kind)
        imports.append((module, name, KINDS[kind]))
    return imports


def read_features(data, position):
    features = []
    count, position = leb(data, position)
    for _ in range(count):
        prefix = chr(data[position])
        name, position = read_name(data, position + 1)
        features.append(prefix + name)
    return features


def main(argv):
    if len(argv) < 2 or any(not text for text in argv[2:]):
        print(__doc__, file=sys.stderr)
        return 64
    with open(argv[1], "rb") as handle:
        data = handle.read()
    if data[:8] != b"\0asm\x01\0\0\0":
        print("not a core WebAssembly module")
        return 1

    position = 8
    sections = []
    custom = []
    imports = []
    memory = None
    tags = 0
    features = []
    while position < len(data):
        section = data[position]
        size, position = leb(data, position + 1)
        name = SECTION_NAMES.get(section, str(section))
        if section == 0:
            title, after = read_name(data, position)
            custom.append(title)
            name = "custom:" + title
            if title == "target_features":
                features = read_features(data, after)
        elif section == 2:
            imports = read_imports(data, position)
        elif section == 5:
            count, cursor = leb(data, position)
            if count:
                minimum, maximum, _ = limits(data, cursor)
                memory = (minimum, maximum)
        elif section == 13:
            tags, _ = leb(data, position)
        sections.append("%s %d" % (name, size))
        position += size

    print("%d bytes" % len(data))
    print("sections: " + ", ".join(sections))
    print("imports (%d): %s" % (len(imports), ", ".join("%s.%s" % (module, name) for module, name, _ in imports)))
    if memory:
        minimum, maximum = memory
        print("memory: %d pages at the start (%d MiB), at most %s" % (
            minimum, minimum * PAGE // 1048576,
            "unstated" if maximum is None else "%d pages (%d MiB)" % (maximum, maximum * PAGE // 1048576)))
    print("exception tags: %d" % tags)
    print("features: " + " ".join(features))

    findings = []
    foreign = ["%s.%s (%s)" % item for item in imports if item[0] != WASI or item[2] != "func" or item[1] not in EXPECTED]
    if foreign:
        findings.append("imports that are not expected: " + ", ".join(foreign))
    seen = {name for module, name, kind in imports if module == WASI and kind == "func"}
    if EXPECTED - seen:
        findings.append("expected imports that are gone: " + ", ".join(sorted(EXPECTED - seen)))
    if tags != 1:
        findings.append("%d exception tags, expected one" % tags)
    if "producers" in custom:
        findings.append("a producers section")
    for text in argv[2:]:
        at = data.find(text.encode("utf-8"))
        if at >= 0:
            findings.append("the text '%s' at byte %d" % (text, at))

    if findings:
        for finding in findings:
            print("NOT AS BUILT: " + finding)
        return 1
    print("the sixteen WASI preview 1 imports and no other; one exception tag; no producers section"
          + ("; none of %d texts of a build machine" % len(argv[2:]) if argv[2:] else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
