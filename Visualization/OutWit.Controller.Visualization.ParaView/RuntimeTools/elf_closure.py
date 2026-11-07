"""DT_NEEDED closure of ELF binaries (author-side helper for trimming the Linux ParaView runtime).

    python3 elf_closure.py <lib-dir> <root1> [<root2> ...]

The Linux twin of pe_closure.py: walks the dynamic sections (DT_NEEDED) starting from the roots
(pvpython-real and the Python extension modules), resolves each needed SONAME against <lib-dir>
(plus its mesa/ and mpi/ subdirectories, which the launcher puts on LD_LIBRARY_PATH) and prints the
libraries of <lib-dir> that are NOT reachable. dlopen'd modules (Mesa itself, Open MPI components,
ParaView plugins) are not seen — the trim is verified by running the corpus, never by this walk.
"""

import os
import struct
import sys


def dynamic_entries(path):
    """(soname, [needed...]) of an ELF shared object / executable; (None, []) for anything else."""
    try:
        with open(path, "rb") as handle:
            data = handle.read()
    except OSError:
        return None, []
    if len(data) < 64 or data[:4] != b"\x7fELF":
        return None, []
    is64 = data[4] == 2
    endian = "<" if data[5] == 1 else ">"
    if is64:
        e_shoff = struct.unpack_from(endian + "Q", data, 0x28)[0]
        e_shentsize, e_shnum = struct.unpack_from(endian + "HH", data, 0x3A)
    else:
        e_shoff = struct.unpack_from(endian + "I", data, 0x20)[0]
        e_shentsize, e_shnum = struct.unpack_from(endian + "HH", data, 0x2E)
    sections = []
    for i in range(e_shnum):
        off = e_shoff + i * e_shentsize
        if off + e_shentsize > len(data):
            return None, []
        if is64:
            _, sh_type, _, _, sh_offset, sh_size, sh_link, _, _, sh_entsize = struct.unpack_from(endian + "IIQQQQIIQQ", data, off)
        else:
            _, sh_type, _, _, sh_offset, sh_size, sh_link, _, _, sh_entsize = struct.unpack_from(endian + "IIIIIIIIII", data, off)
        sections.append((sh_type, sh_offset, sh_size, sh_link, sh_entsize))
    soname, needed = None, []
    for sh_type, sh_offset, sh_size, sh_link, sh_entsize in sections:
        if sh_type != 6 or sh_link >= len(sections):
            continue
        _, str_off, _, _, _ = sections[sh_link]
        entsize = sh_entsize or (16 if is64 else 8)
        for pos in range(sh_offset, min(sh_offset + sh_size, len(data) - entsize + 1), entsize):
            d_tag, d_val = struct.unpack_from(endian + ("qQ" if is64 else "iI"), data, pos)
            if d_tag == 0:
                break
            if d_tag in (1, 14):
                start = str_off + d_val
                end = data.find(b"\0", start)
                if end < 0:
                    continue
                text = data[start:end].decode("ascii", "replace")
                if d_tag == 1:
                    needed.append(text)
                else:
                    soname = text
    return soname, needed


def main(argv):
    lib_dir = os.path.abspath(argv[0])
    roots = [os.path.abspath(r) for r in argv[1:]]
    available = {}
    for sub in ("", "mesa", "mpi"):
        folder = os.path.join(lib_dir, sub)
        if not os.path.isdir(folder):
            continue
        for name in os.listdir(folder):
            path = os.path.join(folder, name)
            if os.path.isfile(path) and ".so" in name:
                available.setdefault(name, path)
    seen = set()
    queue = list(roots)
    while queue:
        current = queue.pop()
        _, needed = dynamic_entries(current)
        for name in needed:
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
        print("   %-70s %8.1f MB" % (os.path.relpath(available[k], lib_dir), size / 1e6))
    print("unreachable total: %.0f MB" % (total / 1e6))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
