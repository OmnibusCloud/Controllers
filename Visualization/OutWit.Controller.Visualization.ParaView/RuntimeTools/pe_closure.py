"""Import closure of Windows PE binaries (author-side helper for trimming the ParaView runtime).

    python pe_closure.py <bin-dir> <root1.exe|.pyd> [<root2> ...]

Walks the static import tables (IMAGE_DIRECTORY_ENTRY_IMPORT) starting from the given roots and
resolves each imported DLL name against <bin-dir>; prints the closure and the DLLs of <bin-dir>
that are NOT reachable from the roots. Delay-loaded and dynamically loaded (LoadLibrary) modules
are not seen — VTK's object factory / plugin loads are dynamic, which is why the trim is verified
by running the corpus, never by this walk alone.
"""

import os
import struct
import sys


def imports_of(path):
    with open(path, "rb") as handle:
        data = handle.read()
    if data[:2] != b"MZ":
        return []
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        return []
    sections_count = struct.unpack_from("<H", data, pe + 6)[0]
    opt_size = struct.unpack_from("<H", data, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    dd_offset = opt + (112 if magic == 0x20B else 96)
    import_rva, import_size = struct.unpack_from("<II", data, dd_offset + 8)
    sections = []
    sec = opt + opt_size
    for i in range(sections_count):
        vsize, vaddr, raw_size, raw_ptr = struct.unpack_from("<IIII", data, sec + 8)
        sections.append((vaddr, max(vsize, raw_size), raw_ptr))
        sec += 40

    def rva_to_off(rva):
        for vaddr, size, raw in sections:
            if vaddr <= rva < vaddr + size:
                return raw + (rva - vaddr)
        return None

    names = []
    if import_rva == 0:
        return names
    off = rva_to_off(import_rva)
    if off is None:
        return names
    while off + 20 <= len(data):
        entry = struct.unpack_from("<IIIII", data, off)
        if entry[3] == 0:
            break
        name_off = rva_to_off(entry[3])
        if name_off is not None and name_off < len(data):
            end = data.find(b"\0", name_off)
            if end < 0:
                end = len(data)
            names.append(data[name_off:end].decode("ascii", "replace"))
        off += 20
        if off + 20 > len(data):
            break
    return names


def main(argv):
    bin_dir = os.path.abspath(argv[0])
    roots = [os.path.abspath(r) for r in argv[1:]]
    available = {f.lower(): os.path.join(bin_dir, f) for f in os.listdir(bin_dir) if f.lower().endswith(".dll")}
    seen = set()
    queue = list(roots)
    while queue:
        current = queue.pop()
        for name in imports_of(current):
            key = name.lower()
            if key in available and key not in seen:
                seen.add(key)
                queue.append(available[key])
    unreachable = sorted(k for k in available if k not in seen)
    print("closure: %d of %d DLLs reachable from %d root(s)" % (len(seen), len(available), len(roots)))
    print("UNREACHABLE (%d):" % len(unreachable))
    for k in unreachable:
        print("  ", k, "%.1f MB" % (os.path.getsize(available[k]) / 1e6))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
