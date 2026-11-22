#!/usr/bin/env python3
"""The PDFs the extractor is tested on, written byte by byte by these scripts alone.

    pdfs.py contract <dir>   the contract set and its expectations (expect.json)
    pdfs.py hostile <dir>    the hostile set: 41 files built to hurt a parser (hostile_pdfs.py)

Nothing third-party: every text, font reference and number comes from here. Standard
library only, Python 3.9 or later; runs under python3 -I (the scripts' own folder is put on
the path for the two modules beside it).
"""

import hashlib
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pdf_writer import HELVETICA, RawWriter, finish, font_resources, latin1, page_dict, text_content  # noqa: E402
from hostile_pdfs import write_hostile  # noqa: E402


THREE_PAGES = [
    "Alpha is the first page of three.",
    "Bravo is the second page, in the middle.",
    "Charlie is the third and last page.",
]
# Four lines: a word broken at a line end (lower case on both sides), then a hyphen that is
# part of a label. pdf_oxide rejoins the first (removing the hyphen and the line end) and
# keeps the second, with an LF between the lines.
HYPHEN_LINES = [
    "The words on this line end in a hyphen-",
    "ated word and the sentence goes on.",
    "A label like COVID-",
    "19 keeps its hyphen at the line end.",
]
HYPHEN_TEXT = "The words on this line end in a hyphenated word and the sentence goes on.\nA label like COVID-\n19 keeps its hyphen at the line end."
EMPTY_PASSWORD_TEXT = "This page is encrypted with the empty user password."

# RC4 with a 128-bit key, Standard security handler revision 3 (ISO 32000-1, 7.6.3).
PASSWORD_PADDING = bytes.fromhex("28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A")
FILE_ID = bytes(range(0x30, 0x40))
PERMISSIONS = -3904


def rc4(key, data):
    state = list(range(256))
    j = 0
    for i in range(256):
        j = (j + state[i] + key[i % len(key)]) & 0xFF
        state[i], state[j] = state[j], state[i]
    out = bytearray()
    i = j = 0
    for byte in data:
        i = (i + 1) & 0xFF
        j = (j + state[i]) & 0xFF
        state[i], state[j] = state[j], state[i]
        out.append(byte ^ state[(state[i] + state[j]) & 0xFF])
    return bytes(out)


def padded(password):
    return (password + PASSWORD_PADDING)[:32]


def rc4_rounds(key, data):
    """RC4 with the key, then 19 more times with the key's bytes XOR 1..19 (revision 3)."""
    data = rc4(key, data)
    for round_number in range(1, 20):
        data = rc4(bytes(byte ^ round_number for byte in key), data)
    return data


def md5_rounds(digest):
    for _ in range(50):
        digest = hashlib.md5(digest[:16]).digest()
    return digest[:16]


def standard_security(user_password, owner_password):
    """(/O, /U, file key) of the Standard handler, revision 3, 128-bit RC4."""
    owner_key = md5_rounds(hashlib.md5(padded(owner_password)).digest())
    owner_entry = rc4_rounds(owner_key, padded(user_password))
    key_hash = hashlib.md5(padded(user_password) + owner_entry + (PERMISSIONS & 0xFFFFFFFF).to_bytes(4, "little") + FILE_ID)
    file_key = md5_rounds(key_hash.digest())
    user_entry = rc4_rounds(file_key, hashlib.md5(PASSWORD_PADDING + FILE_ID).digest()) + bytes(16)
    return owner_entry, user_entry, file_key


def object_key(file_key, number):
    return hashlib.md5(file_key + number.to_bytes(3, "little") + (0).to_bytes(2, "little")).digest()[:16]


def encrypted_document(user_password, text):
    owner_entry, user_entry, file_key = standard_security(user_password, b"the owner's password")
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = writer.reserve()
    writer.set_stream(content, "", rc4(object_key(file_key, content), text_content(text)))
    page = writer.add(page_dict(tree, font_resources(font), content))
    encrypt = writer.add("<< /Filter /Standard /V 2 /R 3 /Length 128 /O <%s> /U <%s> /P %d >>" % (
        owner_entry.hex().upper(), user_entry.hex().upper(), PERMISSIONS))
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%d 0 R] /Count 1 >>" % page)
    return writer.build(catalog, trailer_extra="/Encrypt %d 0 R /ID [<%s> <%s>] " % (encrypt, FILE_ID.hex().upper(), FILE_ID.hex().upper()))


def document(pages):
    """A plain document, one Helvetica text line per page (an empty string: no content)."""
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    kids = []
    for text in pages:
        content = writer.add_stream("", text_content(text) if text else b"")
        kids.append("%d 0 R" % writer.add(page_dict(tree, font_resources(font), content)))
    return finish(writer, catalog, tree, " ".join(kids), len(pages))


def three_pages_nested():
    """Three pages whose objects are written last page first, under a two-level page tree:
    the document order is the tree's, not the order of the objects in the file."""
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    branch = writer.reserve()
    font = writer.add(HELVETICA)
    numbers = {}
    for index in (2, 1, 0):
        content = writer.add_stream("", text_content(THREE_PAGES[index]))
        parent = tree if index == 2 else branch
        numbers[index] = writer.add(page_dict(parent, font_resources(font), content))
    writer.set(branch, "<< /Type /Pages /Parent %d 0 R /Kids [%d 0 R %d 0 R] /Count 2 >>" % (tree, numbers[0], numbers[1]))
    return finish(writer, catalog, tree, "%d 0 R %d 0 R" % (branch, numbers[2]), 3)


def hyphenated():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    lines = " 0 -16 Td ".join("(%s) Tj" % line for line in HYPHEN_LINES)
    content = writer.add_stream("", latin1("BT /F1 12 Tf 72 720 Td %s ET" % lines))
    page = writer.add(page_dict(tree, font_resources(font), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def control_characters():
    """One page whose strings hold a carriage return and a form feed (octal escapes)."""
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = writer.add_stream("", latin1("BT /F1 12 Tf 72 720 Td (carriage\\015return) Tj 0 -16 Td (form\\014feed) Tj ET"))
    page = writer.add(page_dict(tree, font_resources(font), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def no_pages():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    return finish(writer, catalog, tree, "", 0)


def write_contract(folder):
    """The contract set: each file with what the extractor must answer. "pages_text" is the
    text of each page after trimming the whitespace around it; "contains" are fragments the
    text must hold, "absent" characters it must not."""
    cases = [
        ("one-page", document(["The only page of a one-page document."]),
         {"exit": 0, "pages": 1, "encrypted": False, "error": None, "pages_text": ["The only page of a one-page document."]}),
        ("three-pages", document(THREE_PAGES),
         {"exit": 0, "pages": 3, "encrypted": False, "error": None, "pages_text": THREE_PAGES}),
        ("three-pages-nested-tree", three_pages_nested(),
         {"exit": 0, "pages": 3, "encrypted": False, "error": None, "pages_text": THREE_PAGES}),
        ("empty-page", document([THREE_PAGES[0], "", THREE_PAGES[2]]),
         {"exit": 0, "pages": 3, "encrypted": False, "error": None, "pages_text": [THREE_PAGES[0], "", THREE_PAGES[2]]}),
        ("hyphenated-line", hyphenated(),
         {"exit": 0, "pages": 1, "encrypted": False, "error": None, "pages_text": [HYPHEN_TEXT]}),
        ("control-characters", control_characters(),
         {"exit": 0, "pages": 1, "encrypted": False, "error": None, "contains": ["carriage", "return", "form", "feed"], "absent": ["\r", "\f"]}),
        ("encrypted-empty-password", encrypted_document(b"", EMPTY_PASSWORD_TEXT),
         {"exit": 0, "pages": 1, "encrypted": True, "error": None, "pages_text": [EMPTY_PASSWORD_TEXT]}),
        ("encrypted-user-password", encrypted_document(b"a user password", "This page needs a password."),
         {"exit": 2, "pages": 0, "encrypted": True, "error": "encrypted"}),
        ("no-pages", no_pages(),
         {"exit": 2, "pages": 0, "encrypted": False, "error": "no_pages"}),
        ("not-a-pdf", latin1("<!DOCTYPE html>\n<html><body><p>This is a web page, not a PDF.</p></body></html>\n"),
         {"exit": 2, "pages": 0, "encrypted": False, "error": "unreadable"}),
        ("empty-file", b"",
         {"exit": 2, "pages": 0, "encrypted": False, "error": "unreadable"}),
    ]
    os.makedirs(folder, exist_ok=True)
    expect = {}
    for name, data, expectation in cases:
        with open(os.path.join(folder, name + ".pdf"), "wb") as handle:
            handle.write(data)
        expect[name + ".pdf"] = expectation
    with open(os.path.join(folder, "expect.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(expect, handle, indent=1, sort_keys=True)
        handle.write("\n")
    return [name + ".pdf" for name, _, _ in cases]


def main(argv):
    if len(argv) != 3 or argv[1] not in ("contract", "hostile"):
        print("usage: pdfs.py contract|hostile <dir>", file=sys.stderr)
        return 64
    names = write_contract(argv[2]) if argv[1] == "contract" else write_hostile(argv[2])
    print("%d files in %s" % (len(names), argv[2]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
