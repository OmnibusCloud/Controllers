"""The third-party notices of what the Verify controller redistributes.

  notices.py write <repository-root> [--cache DIR]
      writes every notices file of notices.json from its pinned sources
  notices.py check <repository-root> [--cache DIR]
      makes every file again in memory and compares it with the committed one; a file
      that differs, or is missing, is named and the exit code is 1

One file a folder that is redistributed: each language runtime's folder of the runtime
set, the folder of the native engine library, and the controller's module. A file lists
the third-party software in its folder and gives the terms of each part. No text is
written from memory: every one is a file, or a named part of a file, of a pinned source
(an address and the SHA-256 the bytes must have), or a file that lies beside the notices
file and is named there. A source that does not hash to its pin stops the run.

The table is notices.json, next to this script. A source is a file by its address, or an
archive the toolchain already pins in one of its pins.env files (the table names the file
and the key, so there is one pin). The native engine library's list of Rust crates is
not in the table: it is read from the engine's own vendored source archive (vendored.py).

Run with python3 -I. Needs Python 3.11 (vendored.py reads manifests with tomllib).
"""

import hashlib
import io
import json
import os
import re
import sys
import tarfile
import tempfile
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLCHAIN = os.path.dirname(HERE)
TABLE = os.path.join(HERE, "notices.json")
RULE = "=" * 80

# The tool's own folder: python3 -I leaves a script's folder off the module path. Nothing is
# written beside the sources for it.
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)
import vendored  # noqa: E402


class Sources:
    """The pinned sources of the table: fetched once, held to their pins, kept in a cache."""

    def __init__(self, table, cache):
        self.table = table
        self.cache = cache
        self.envs = {}
        self.members = {}
        os.makedirs(cache, exist_ok=True)

    def pin(self, name):
        """The address and the SHA-256 of a source."""
        source = self.table[name]
        if "env" in source:
            env = self.env(source["env"])
            return env[source["key"] + "_URL"], env[source["key"] + "_SHA256"]
        return source["url"], source["sha256"]

    def env(self, relative):
        if relative not in self.envs:
            values = {}
            with open(os.path.join(TOOLCHAIN, relative), encoding="utf-8") as lines:
                for line in lines:
                    match = re.match(r"^([A-Z0-9_]+)=(.*)$", line.strip())
                    if match:
                        values[match.group(1)] = match.group(2)
            self.envs[relative] = values
        return self.envs[relative]

    def path(self, name):
        """The local file of a source: from the cache when it is there and hashes to its pin, else fetched."""
        url, pin = self.pin(name)
        if not re.fullmatch("[0-9a-f]{64}", pin):
            sys.exit(f"{name}: no pin")
        path = os.path.join(self.cache, pin + "-" + re.sub("[^A-Za-z0-9._+-]", "_", url.rsplit("/", 1)[-1]))
        if not os.path.isfile(path) or sha256_of(path) != pin:
            request = urllib.request.Request(url, headers={"User-Agent": "verify-notices"})
            with urllib.request.urlopen(request, timeout=600) as response, open(path + ".part", "wb") as part:
                while True:
                    block = response.read(1 << 20)
                    if not block:
                        break
                    part.write(block)
            seen = sha256_of(path + ".part")
            if seen != pin:
                os.remove(path + ".part")
                sys.exit(f"{name}: {url} hashes to {seen}, pinned {pin}")
            os.replace(path + ".part", path)
        return path

    def file(self, name):
        with open(self.path(name), "rb") as source:
            return source.read()

    def member(self, name, member):
        """One file of an archive; every member the table asks of an archive is read in one pass."""
        if name not in self.members:
            wanted = set()
            for document in DOCUMENTS:
                for part in parts_of(document):
                    for text in part.get("texts", []):
                        if text.get("from") == name and "member" in text:
                            wanted.add(text["member"])
            self.members[name] = read_members(self.path(name), self.pin(name)[0], wanted)
        if member not in self.members[name]:
            sys.exit(f"{name}: no member {member}")
        return self.members[name][member]


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def read_members(path, url, wanted):
    found = {}
    if url.endswith((".zip", ".whl")):
        with zipfile.ZipFile(path) as archive:
            for member in wanted:
                if member in archive.namelist():
                    found[member] = archive.read(member)
        return found
    with tarfile.open(path, "r:*") as archive:
        for entry in archive:
            if entry.isfile() and entry.name in wanted:
                found[entry.name] = archive.extractfile(entry).read()
    return found


def plain(data):
    """A text as it is printed: its own characters, LF line ends, one line end at its end."""
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        text = data.decode("latin-1")
    text = text.lstrip("﻿").replace("\r\n", "\n").replace("\r", "\n")
    return text.rstrip("\n") + "\n"


def head_comment(text):
    """The comment a C file starts with, without its comment marks."""
    match = re.match(r"\s*/\*(.*?)\*/", text, re.S)
    if not match:
        sys.exit("no comment at the head of a file the table cuts one from")
    lines = []
    for line in match.group(1).split("\n"):
        line = re.sub(r"^\s*\*? ?", "", line.rstrip())
        lines.append(line)
    return "\n".join(lines).strip("\n") + "\n"


def rst_block(text, heading):
    """The first literal block under a heading of a reStructuredText file, without its indent."""
    lines = text.split("\n")
    at = None
    for index in range(len(lines) - 1):
        if lines[index] == heading and re.fullmatch(r"[-=~^]{3,}", lines[index + 1]):
            at = index + 2
            break
    if at is None:
        sys.exit(f"no heading '{heading}'")
    while not lines[at].endswith("::"):
        at += 1
    at += 1
    block = []
    while at < len(lines) and (lines[at] == "" or lines[at].startswith(" ")):
        block.append(lines[at])
        at += 1
    indent = min(len(line) - len(line.lstrip(" ")) for line in block if line)
    return "\n".join(line[indent:] for line in block).strip("\n") + "\n"


def from_line(text, line):
    """A file from one of its lines to its end."""
    at = text.find("\n" + line + "\n")
    if at < 0:
        sys.exit(f"no line '{line}'")
    return text[at + 1:].rstrip("\n") + "\n"


def cut(text, how):
    if how is None:
        return text
    if how["kind"] == "head-comment":
        return head_comment(text)
    if how["kind"] == "rst-block":
        return rst_block(text, how["heading"])
    if how["kind"] == "from-line":
        return from_line(text, how["line"])
    sys.exit(f"unknown cut {how['kind']}")


def describe_cut(how):
    if how is None:
        return ""
    if how["kind"] == "head-comment":
        return "the comment at the head of "
    if how["kind"] == "rst-block":
        return f"the text under the heading \"{how['heading']}\" of "
    return f"from the line \"{how['line']}\" to the end of "


def parts_of(document):
    parts = []
    for part in document["parts"]:
        if "group" in part:
            # A group is written once for every module that links it: the module's name is put in.
            for shared in GROUPS[part["group"]]:
                parts.append(dict(shared, **{"in": shared["in"].replace("{module}", document["module"])}))
        else:
            parts.append(part)
    return parts


def render_text(sources, text):
    """Where a text comes from, in words, and the text."""
    name = text["from"]
    url, pin = sources.pin(name)
    how = text.get("cut")
    if "member" in text:
        body = cut(plain(sources.member(name, text["member"])), how)
        inside = text["member"].split("/", 1)[1] if text.get("strip", True) else text["member"]
        origin = f"{describe_cut(how)}{inside} in {url}\n(SHA-256 of that archive: {pin})"
    else:
        body = cut(plain(sources.file(name)), how)
        origin = f"{describe_cut(how)}{url}\n(SHA-256 of that file: {pin})"
    return origin, body


def render(sources, document):
    parts = parts_of(document)
    out = io.StringIO()
    out.write("THIRD-PARTY NOTICES\n")
    out.write(document["title"] + "\n\n")
    for paragraph in document["about"] + INTRODUCTION:
        out.write(wrapped(paragraph) + "\n\n")

    out.write("Parts\n-----\n")
    for number, part in enumerate(parts, 1):
        out.write(f"{number:3}. {title_of(part)}: {part['licence']}\n")
    out.write("\n")

    for number, part in enumerate(parts, 1):
        out.write(f"{RULE}\n{number}. {title_of(part)}\n{RULE}\n")
        out.write(f"In:       {part['in']}\n")
        out.write(f"Licence:  {part['licence']}\n")
        for note in part.get("notes", []):
            out.write(f"Note:     {note}\n")
        for beside in part.get("files", []):
            out.write(f"Text:     {beside}, beside this file\n")
        if "crates" in part:
            out.write("\n")
            out.write(crates(sources, part["crates"]))
            continue
        texts = part.get("texts", [])
        for index, text in enumerate(texts, 1):
            origin, body = render_text(sources, text)
            label = "Text" if len(texts) == 1 else f"Text {index} of {len(texts)}"
            out.write(f"\n{label}: {origin}\n\n{body}")
        out.write("\n")
    return out.getvalue()


def title_of(part):
    version = part.get("version")
    if not version:
        return part["name"]
    return f"{part['name']} {version}" if version[0].isdigit() else f"{part['name']} ({version})"


# The crates of the native engine library ------------------------------------------------


def crates(sources, spec):
    """The Rust crates the engine's C API library is made of, with their licence texts.

    Read from the engine's vendored source archive: its lock file names every crate and
    version, its manifests say which dependencies are linked (not build scripts, not
    procedural macros), which are optional and which features turn them on, and for which
    platforms. The walk starts at the C API library with its default features and is made
    once for each platform the controller ships. It follows manifests, not the compiler: a
    crate the linker dropped can still be listed.
    """
    url, pin = sources.pin(spec["from"])
    manifests, lock, texts, workspace = vendored.read(sources.path(spec["from"]))
    reached = {}
    for platform, target in vendored.TARGETS.items():
        for key in vendored.resolve(spec["root"], target, manifests, lock, workspace):
            reached.setdefault(key, []).append(platform)

    own = sorted(key for key in reached if lock[key].get("source") is None)
    foreign = sorted(key for key in reached if lock[key].get("source") is not None)

    out = io.StringIO()
    for paragraph in spec["about"]:
        out.write(wrapped(paragraph) + "\n\n")
    out.write(f"Source: {url}\n(SHA-256 of that archive: {pin})\nA text below is named by its place in that archive.\n\n")
    out.write(f"The engine's own crates ({len(own)}), under the engine's licence:\n")
    out.write(wrapped(", ".join(f"{name} {version}" for name, version in own)) + "\n\n")

    # Every text once: crates that carry the same text share it.
    numbers = {}
    order = []
    rows = []
    for key in foreign:
        package, folder, manifest = manifests[key]
        licence = package.get("license") or f"see {package.get('license-file', 'its package')}"
        mine = []
        for path in sorted(texts.get(folder, {})):
            body = plain(texts[folder][path])
            digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
            if digest not in numbers:
                numbers[digest] = len(order) + 1
                order.append((body, []))
            order[numbers[digest] - 1][1].append(f"{folder}/{path}")
            if numbers[digest] not in mine:
                mine.append(numbers[digest])
        where = "" if len(reached[key]) == len(vendored.TARGETS) else " [" + ", ".join(reached[key]) + "]"
        named = ", ".join(f"T{number}" for number in mine) if mine else "none in its package"
        rows.append(f"  {key[0]} {key[1]}{where}: {licence}; texts: {named}")

    out.write(f"Crates of others ({len(foreign)}). After a crate: the platforms it is for when not all three, the\n")
    out.write("licence its manifest states, and the texts its package carries (below, each printed once):\n\n")
    out.write("\n".join(rows) + "\n\n")
    for number, (body, paths) in enumerate(order, 1):
        out.write(f"{'-' * 80}\nT{number}: {paths[0]}\n")
        if len(paths) > 1:
            out.write(wrapped("The same text: " + ", ".join(paths[1:])) + "\n")
        out.write(f"{'-' * 80}\n{body}\n")
    return out.getvalue()


def wrapped(text, width=96):
    lines = []
    line = ""
    for word in text.split(" "):
        if line and len(line) + 1 + len(word) > width:
            lines.append(line)
            line = word
        else:
            line = word if not line else line + " " + word
    return "\n".join(lines + [line])


# ----------------------------------------------------------------------------------------


def main(arguments):
    if len(arguments) < 2 or arguments[0] not in ("write", "check"):
        sys.exit(__doc__)
    root = os.path.abspath(arguments[1])
    cache = os.path.join(tempfile.gettempdir(), "verify-notices")
    if "--cache" in arguments:
        cache = os.path.abspath(arguments[arguments.index("--cache") + 1])
    sources = Sources(SOURCES, cache)

    differing = 0
    for document in DOCUMENTS:
        made = render(sources, document).encode("utf-8")
        path = os.path.join(root, *document["path"].split("/"))
        digest = hashlib.sha256(made).hexdigest()
        if arguments[0] == "write":
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as target:
                target.write(made)
            print(f"wrote    {digest}  {len(made):8}  {document['path']}")
        elif not os.path.isfile(path):
            print(f"MISSING  {document['path']}")
            differing += 1
        else:
            with open(path, "rb") as committed:
                same = committed.read() == made
            print(f"{'same    ' if same else 'DIFFERS '} {digest}  {len(made):8}  {document['path']}")
            differing += 0 if same else 1
    if differing:
        print(f"{differing} notices file(s) are not what the pinned sources give: run notices.py write and commit the result")
        sys.exit(1)


with open(TABLE, encoding="utf-8") as table:
    _TABLE = json.load(table)
SOURCES = _TABLE["sources"]
GROUPS = _TABLE["groups"]
DOCUMENTS = _TABLE["documents"]
INTRODUCTION = _TABLE["introduction"]

if __name__ == "__main__":
    main(sys.argv[1:])
