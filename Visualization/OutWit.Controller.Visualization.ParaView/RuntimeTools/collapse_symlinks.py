"""Replaces the symlink chains of a Linux/macOS ParaView tree with one regular file per shared library.

    python3 collapse_symlinks.py <runtime-root> [--pseudo]

Zip archives (and the engine's asset extractor, which unpacks them with .NET's ZipFile) carry no
symlinks, and storing every alias of a library as a copy would triple the archive. Dynamic loaders
resolve dependencies by the library's own name — DT_SONAME on ELF, the basename of LC_ID_DYLIB on
Mach-O — so for every real library this keeps exactly one regular file under that name and deletes
every other alias (the unversioned development link, the fully versioned name); an alias that is a
full byte-identical copy (the bundled Mesa ships its aliases as copies) is deleted too. Libraries
without an embedded name keep their file name. Non-library symlinks are replaced by copies.

--pseudo additionally treats the files 7-Zip writes on Windows when extracting HFS+ symlinks
(a tiny regular file whose content is the link target path) as symlinks, which is how the macOS
bundle is processed on the author's Windows machine.
"""

import filecmp
import os
import shutil
import struct
import sys

PSEUDO_LINK_MAX_BYTES = 1024


def elf_soname(data):
    """DT_SONAME of an ELF shared object, or None."""
    if len(data) < 64 or data[:4] != b"\x7fELF":
        return None
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
            return None
        if is64:
            _, sh_type, _, _, sh_offset, sh_size, sh_link, _, _, sh_entsize = struct.unpack_from(endian + "IIQQQQIIQQ", data, off)
        else:
            _, sh_type, _, _, sh_offset, sh_size, sh_link, _, _, sh_entsize = struct.unpack_from(endian + "IIIIIIIIII", data, off)
        sections.append((sh_type, sh_offset, sh_size, sh_link, sh_entsize))
    for sh_type, sh_offset, sh_size, sh_link, sh_entsize in sections:
        if sh_type != 6 or sh_link >= len(sections):  # SHT_DYNAMIC
            continue
        _, str_off, _, _, _ = sections[sh_link]
        entsize = sh_entsize or (16 if is64 else 8)
        for pos in range(sh_offset, min(sh_offset + sh_size, len(data) - entsize + 1), entsize):
            d_tag, d_val = struct.unpack_from(endian + ("qQ" if is64 else "iI"), data, pos)
            if d_tag == 0:
                break
            if d_tag == 14:  # DT_SONAME
                start = str_off + d_val
                end = data.find(b"\0", start)
                return data[start:end].decode("ascii", "replace") if end > start else None
    return None


def macho_id(data):
    """Basename of LC_ID_DYLIB of a Mach-O dylib (first slice of a fat file), or None."""
    if len(data) < 32:
        return None
    magic = struct.unpack_from(">I", data, 0)[0]
    if magic == 0xCAFEBABE:  # fat, big-endian header
        nfat = struct.unpack_from(">I", data, 4)[0]
        if nfat == 0:
            return None
        _, _, offset, size, _ = struct.unpack_from(">IIIII", data, 8)
        return macho_id(data[offset:offset + size])
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
        return None
    ncmds = struct.unpack_from(endian + "I", data, 16)[0]
    pos = header
    for _ in range(ncmds):
        if pos + 8 > len(data):
            return None
        cmd, cmdsize = struct.unpack_from(endian + "II", data, pos)
        if cmdsize < 8:
            return None
        if cmd == 0xD:  # LC_ID_DYLIB
            name_off = struct.unpack_from(endian + "I", data, pos + 8)[0]
            start = pos + name_off
            end = data.find(b"\0", start, pos + cmdsize)
            name = data[start:end if end > 0 else pos + cmdsize].decode("utf-8", "replace")
            return os.path.basename(name) or None
        pos += cmdsize
    return None


def library_name_of(path):
    """The name the loader resolves this library by (SONAME / LC_ID_DYLIB basename), or None."""
    try:
        with open(path, "rb") as handle:
            data = handle.read()
    except OSError:
        return None
    return elf_soname(data) or macho_id(data)


def looks_like_library(name):
    return ".so" in name or ".dylib" in name


def pseudo_link_target(path):
    """Target of a 7-Zip pseudo symlink (a tiny text file naming an existing relative path), or None."""
    try:
        if os.path.getsize(path) > PSEUDO_LINK_MAX_BYTES:
            return None
        with open(path, "rb") as handle:
            content = handle.read()
    except OSError:
        return None
    if not content or b"\0" in content or b"\n" in content.strip():
        return None
    try:
        text = content.decode("utf-8").strip()
    except UnicodeDecodeError:
        return None
    if not text or text.startswith("/") or any(ch in text for ch in "*?<>|\""):
        return None
    target = os.path.normpath(os.path.join(os.path.dirname(path), text))
    if not os.path.exists(target) or os.path.abspath(target) == os.path.abspath(path):
        return None
    return target


def resolve_pseudo_chain(path):
    """Follows alias -> alias -> real file chains of pseudo links."""
    seen = {os.path.abspath(path)}
    target = pseudo_link_target(path)
    while target is not None and os.path.isfile(target) and os.path.abspath(target) not in seen:
        seen.add(os.path.abspath(target))
        nxt = pseudo_link_target(target)
        if nxt is None:
            break
        target = nxt
    return target


def main(argv):
    pseudo = "--pseudo" in argv
    positional = [a for a in argv if not a.startswith("--")]
    if not positional:
        print(__doc__)
        return 2
    root = os.path.abspath(positional[0])
    replaced = removed = copied = 0

    def links_in(dirpath, names):
        for name in names:
            path = os.path.join(dirpath, name)
            if os.path.islink(path):
                yield path, os.path.realpath(path)
            elif pseudo and os.path.isfile(path):
                target = resolve_pseudo_chain(path)
                if target is not None:
                    yield path, target

    for dirpath, dirnames, filenames in os.walk(root):
        for path, target in list(links_in(dirpath, list(filenames) + list(dirnames))):
            name = os.path.basename(path)
            if not os.path.exists(target):
                os.remove(path)
                removed += 1
                continue
            if os.path.isdir(target):
                os.remove(path)
                shutil.copytree(target, path)
                copied += 1
                continue
            library_name = library_name_of(target) if looks_like_library(name) else None
            if library_name is None:
                os.remove(path)
                shutil.copy2(target, path)
                copied += 1
                continue
            wanted = os.path.join(os.path.dirname(target), library_name)
            os.remove(path)
            removed += 1
            if not os.path.exists(wanted):
                os.rename(target, wanted)
                replaced += 1

    # Second pass: libraries whose real file name is not their loader name. Without an alias they are
    # renamed; when the loader-named file exists and is byte-identical the duplicate is deleted; a
    # differing file is left alone and reported.
    deduped = 0
    for dirpath, _, filenames in os.walk(root):
        for name in sorted(filenames):
            path = os.path.join(dirpath, name)
            if os.path.islink(path) or not looks_like_library(name):
                continue
            library_name = library_name_of(path)
            if not library_name or library_name == name:
                continue
            wanted = os.path.join(dirpath, library_name)
            if not os.path.exists(wanted):
                os.rename(path, wanted)
                replaced += 1
            elif filecmp.cmp(path, wanted, shallow=False):
                os.remove(path)
                deduped += 1
            else:
                print("  ! %s is %s to its loader but differs from the existing file; kept both" % (os.path.relpath(path, root), library_name))
    print("links removed: %d, libraries renamed to their loader name: %d, duplicate alias copies deleted: %d, non-library links copied: %d" % (removed, replaced, deduped, copied))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
