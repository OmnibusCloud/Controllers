#!/usr/bin/env python3
"""The hostile set: 41 PDFs built to hurt a parser, the ones the PDF spike measured, case
for case and object for object (a Flate stream's compressed bytes depend on the zlib that
writes them; what they decode to does not). Decompression bombs in each kind of stream a
text extractor decodes, nesting, reference cycles, page-tree amplification, broken and lying
cross-reference data, truncation, pathological fonts and content streams, and two sane
documents as the baseline.

Imported by pdfs.py (pdfs.py hostile <dir>).
"""

import os
import sys

from pdf_writer import (GIB, HELVETICA, PAGE_BOX, RawWriter, deflate, finish, font_resources, latin1,
                        page_dict, text_content)


def single_page(contents, page_extra, resources=None):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = contents(writer)
    page = writer.add("<< /Type /Page /Parent %d 0 R %s /Resources %s /Contents %d 0 R %s >>" % (
        tree, PAGE_BOX, resources if resources is not None else font_resources(font), content, page_extra))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def plain(writer, content):
    return writer.add_stream("", latin1(content))


def spike_document(pages, text):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    kids = []
    for page in range(1, pages + 1):
        content = writer.add_stream("", text_content(text(page)))
        kids.append("%d 0 R " % writer.add(page_dict(tree, font_resources(font), content)))
    return finish(writer, catalog, tree, "".join(kids), pages)


def rebuild3(build):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    kids = []
    for page in range(1, 4):
        content = writer.add_stream("", text_content("Page %d with broken cross references." % page))
        kids.append("%d 0 R " % writer.add(page_dict(tree, font_resources(font), content)))
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%s] /Count 3 >>" % "".join(kids))
    return build(writer, catalog)


def one_font_page(font_body, content_text):
    """A page whose font F1 is the object font_body(writer) returns."""
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = font_body(writer)
    content = plain(writer, content_text)
    page = writer.add(page_dict(tree, font_resources(font), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


WIDTHS = " ".join(["500"] * 95)


def bomb_tounicode(deflated):
    def font(writer):
        cmap = writer.add_stream("/Filter /FlateDecode", deflated)
        return writer.add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode %d 0 R >>" % cmap)
    return one_font_page(font, "BT /F1 12 Tf 72 720 Td (to unicode bomb) Tj ET")


def bomb_fontfile(deflated):
    def font(writer):
        file_number = writer.add_stream("/Filter /FlateDecode /Length1 1073741824", deflated)
        descriptor = writer.add("<< /Type /FontDescriptor /FontName /Bomb /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 %d 0 R >>" % file_number)
        return writer.add("<< /Type /Font /Subtype /TrueType /BaseFont /Bomb /FirstChar 32 /LastChar 126 /Widths [%s] /Encoding /WinAnsiEncoding /FontDescriptor %d 0 R >>" % (WIDTHS, descriptor))
    return one_font_page(font, "BT /F1 12 Tf 72 720 Td (font file bomb) Tj ET")


def bomb_image(deflated):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    image = writer.add_stream("/Type /XObject /Subtype /Image /Width 32768 /Height 32768 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", deflated)
    content = plain(writer, "q 612 0 0 792 0 0 cm /Im1 Do Q BT /F1 12 Tf 72 720 Td (image bomb, text after) Tj ET")
    page = writer.add(page_dict(tree, "<< /Font << /F1 %d 0 R >> /XObject << /Im1 %d 0 R >> >>" % (font, image), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def bomb_object_stream():
    # Objects 1 (catalog) and 2 (page tree) live in object stream 5, padded to 1 GiB after them.
    writer = RawWriter()
    writer.reserve()
    writer.reserve()
    font = writer.add(HELVETICA)
    content = plain(writer, "BT /F1 12 Tf 72 720 Td (object stream bomb) Tj ET")
    object_stream = writer.reserve()
    page = writer.add("<< /Type /Page /Parent 2 0 R %s /Resources %s /Contents %d 0 R >>" % (PAGE_BOX, font_resources(font), content))
    catalog = "<< /Type /Catalog /Pages 2 0 R >>"
    tree = "<< /Type /Pages /Kids [%d 0 R] /Count 1 >>" % page
    header = latin1("1 0 2 %d " % (len(catalog) + 1))
    body = latin1(catalog + "\n" + tree + "\n")
    writer.set_stream(object_stream, "/Type /ObjStm /N 2 /First %d /Filter /FlateDecode" % len(header), deflate(header + body, GIB, ord(" ")))
    return writer.build_with_xref_stream(1, [(1, object_stream, 0), (2, object_stream, 1)])


def cycle_self_contents():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    contents = writer.reserve()
    writer.set(contents, "%d 0 R" % contents)
    page = writer.add(page_dict(tree, font_resources(font), contents))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def cycle_page_tree():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    inner = writer.reserve()
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%d 0 R] /Count 1 >>" % inner)
    writer.set(inner, "<< /Type /Pages /Parent %d 0 R /Kids [%d 0 R %d 0 R] /Count 1 >>" % (tree, tree, inner))
    return writer.build(catalog)


def cycle_parent_resources():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    content = plain(writer, "BT /F1 12 Tf 72 720 Td (inherited resources) Tj ET")
    page = writer.reserve()
    middle = writer.reserve()
    writer.set(page, "<< /Type /Page /Parent %d 0 R %s /Contents %d 0 R >>" % (middle, PAGE_BOX, content))
    writer.set(middle, "<< /Type /Pages /Parent %d 0 R /Kids [%d 0 R] /Count 1 >>" % (page, page))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def form_cycle(pair):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    first = writer.reserve()
    second = writer.reserve() if pair else first
    form = "/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /Font << /F1 %d 0 R >> /XObject << /Fm %d 0 R >> >>"
    writer.set_stream(first, form % (font, second), latin1("BT /F1 12 Tf 72 720 Td (form) Tj ET /Fm Do"))
    if pair:
        writer.set_stream(second, form % (font, first), latin1("BT /F1 12 Tf 72 700 Td (pair) Tj ET /Fm Do"))
    content = plain(writer, "/Fm Do")
    page = writer.add(page_dict(tree, "<< /Font << /F1 %d 0 R >> /XObject << /Fm %d 0 R >> >>" % (font, first), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def cycle_length_self():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = writer.reserve()
    writer.set(content, "<< /Length %d 0 R >>\nstream\nBT /F1 12 Tf 72 720 Td (length is itself) Tj ET\nendstream" % content)
    page = writer.add(page_dict(tree, font_resources(font), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def form_fanout(fanout, depth):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    forms = [writer.reserve() for _ in range(depth + 1)]
    for level in range(depth):
        writer.set_stream(forms[level], "/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /XObject << /Fm %d 0 R >> >>" % forms[level + 1], latin1("/Fm Do " * fanout))
    writer.set_stream(forms[depth], "/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /Font << /F1 %d 0 R >> >>" % font, latin1("BT /F1 12 Tf 72 720 Td (x) Tj ET"))
    content = plain(writer, "/Fm Do")
    page = writer.add(page_dict(tree, "<< /Font << /F1 %d 0 R >> /XObject << /Fm %d 0 R >> >>" % (font, forms[0]), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def pages_count_lie():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = plain(writer, "BT /F1 12 Tf 72 720 Td (one page, two billion claimed) Tj ET")
    page = writer.add(page_dict(tree, font_resources(font), content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 2147483647)


def pages_fanout(outer, inner):
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    middle = writer.reserve()
    font = writer.add(HELVETICA)
    content = plain(writer, "BT /F1 12 Tf 72 720 Td (the same page again) Tj ET")
    page = writer.add(page_dict(middle, font_resources(font), content))
    writer.set(middle, "<< /Type /Pages /Parent %d 0 R /Kids [%s] /Count %d >>" % (tree, ("%d 0 R " % page) * inner, inner))
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%s] /Count %d >>" % (("%d 0 R " % middle) * outer, outer * inner))
    return writer.build(catalog)


def xref_stream_huge_size():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = plain(writer, "BT /F1 12 Tf 72 720 Td (an xref stream that claims two billion objects) Tj ET")
    page = writer.add(page_dict(tree, font_resources(font), content))
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%d 0 R] /Count 1 >>" % page)
    return writer.build_with_xref_stream(catalog, [], declared_size=2000000000)


def object_number_huge():
    head = "%PDF-1.7\n"
    catalog = "1 0 obj\n<< /Type /Catalog /Pages 2147483646 0 R >>\nendobj\n"
    tree = "2147483646 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n"
    xref = len(head) + len(catalog) + len(tree)
    return latin1(head + catalog + tree
                  + "xref\n0 2\n0000000000 65535 f \n%010d 00000 n \n2147483646 1\n%010d 00000 n \n" % (len(head), len(head) + len(catalog))
                  + "trailer\n<< /Size 2147483647 /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF\n" % xref)


def font_truetype_garbage():
    font = bytearray(12 + 16 * 4)
    font[0:4] = bytes([0, 1, 0, 0])
    font[4:6] = bytes([0xFF, 0xFF])
    for index, tag in enumerate(["head", "loca", "glyf", "cmap"]):
        at = 12 + index * 16
        font[at:at + 4] = latin1(tag)
        font[at + 8:at + 12] = bytes([0, 0, 0, 12])
        font[at + 12:at + 16] = bytes([0x7F, 0xFF, 0xFF, 0xFF])

    def body(writer):
        file_number = writer.add_stream("/Length1 %d" % len(font), bytes(font))
        descriptor = writer.add("<< /Type /FontDescriptor /FontName /Garbage /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 %d 0 R >>" % file_number)
        return writer.add("<< /Type /Font /Subtype /TrueType /BaseFont /Garbage /FirstChar 32 /LastChar 126 /Widths [%s] /Encoding /WinAnsiEncoding /FontDescriptor %d 0 R >>" % (WIDTHS, descriptor))
    return one_font_page(body, "BT /F1 12 Tf 72 720 Td (garbage truetype tables) Tj ET")


def cff_index(items):
    output = bytearray(len(items).to_bytes(2, "big"))
    if not items:
        return bytes(output)
    output.append(4)
    offset = 1
    output += offset.to_bytes(4, "big")
    for item in items:
        offset += len(item)
        output += offset.to_bytes(4, "big")
    for item in items:
        output += item
    return bytes(output)


def cff_recursive(glyphs):
    """A bare CFF font whose every charstring calls global subroutine 0, which calls itself."""
    def int32_operand(value):
        return bytes([29]) + value.to_bytes(4, "big")

    header = bytes([1, 0, 4, 4])
    name = cff_index([latin1("Recursive")])
    strings = cff_index([])
    global_subrs = cff_index([bytes([32, 29, 11])])
    char_strings = cff_index([bytes([32, 29, 14])] * glyphs)
    private_dict = bytes([139, 20, 139, 21])
    # Top DICT: CharStrings (17) and Private (18) with fixed-width operands, so its size is known up front.
    top_size = 5 + 1 + 5 + 5 + 1
    top_index_size = 2 + 1 + 8 + top_size
    char_strings_offset = len(header) + len(name) + top_index_size + len(strings) + len(global_subrs)
    private_offset = char_strings_offset + len(char_strings)
    top = int32_operand(char_strings_offset) + bytes([17]) + int32_operand(len(private_dict)) + int32_operand(private_offset) + bytes([18])
    return header + name + cff_index([top]) + strings + global_subrs + char_strings + private_dict


def font_cff_subr_recursion():
    cff = cff_recursive(230)

    def body(writer):
        file_number = writer.add_stream("/Subtype /Type1C", cff)
        descriptor = writer.add("<< /Type /FontDescriptor /FontName /Recursive /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 %d 0 R >>" % file_number)
        return writer.add("<< /Type /Font /Subtype /Type1 /BaseFont /Recursive /FirstChar 32 /LastChar 126 /Widths [%s] /FontDescriptor %d 0 R >>" % (WIDTHS, descriptor))
    return one_font_page(body, "BT /F1 12 Tf 72 720 Td (recursive charstrings) Tj ET")


def font_type3_recursive():
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.reserve()
    glyph = writer.add_stream("", latin1("1000 0 d0 BT /T3 1 Tf (a) Tj ET"))
    writer.set(font, "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a %d 0 R >> /Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] /Resources << /Font << /T3 %d 0 R >> >> >>" % (glyph, font))
    content = plain(writer, "BT /T3 12 Tf 72 720 Td (aaaa) Tj ET")
    page = writer.add(page_dict(tree, "<< /Font << /T3 %d 0 R >> >>" % font, content))
    return finish(writer, catalog, tree, "%d 0 R" % page, 1)


def encrypted_unknown_password():
    owner = bytes((index * 7 + 3) & 0xFF for index in range(32)).hex().upper()
    user = bytes((index * 13 + 5) & 0xFF for index in range(32)).hex().upper()
    writer = RawWriter()
    catalog = writer.reserve()
    tree = writer.reserve()
    font = writer.add(HELVETICA)
    content = writer.add_stream("", bytes((index * 31 + 17) & 0xFF for index in range(64)))
    page = writer.add(page_dict(tree, font_resources(font), content))
    encrypt = writer.add("<< /Filter /Standard /V 2 /R 3 /Length 128 /O <%s> /U <%s> /P -4 >>" % (owner, user))
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%d 0 R] /Count 1 >>" % page)
    return writer.build(catalog, trailer_extra="/Encrypt %d 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] " % encrypt)


class LzwBits:
    """LZW codes packed most significant bit first, as PDF's LZWDecode reads them."""

    def __init__(self):
        self.output = bytearray()
        self.buffer = 0
        self.count = 0

    def write(self, code, width):
        for bit in range(width - 1, -1, -1):
            self.buffer = ((self.buffer << 1) | ((code >> bit) & 1)) & 0xFF
            self.count += 1
            if self.count == 8:
                self.output.append(self.buffer)
                self.buffer = 0
                self.count = 0

    def to_bytes(self):
        if self.count > 0:
            self.output.append((self.buffer << (8 - self.count)) & 0xFF)
            self.buffer = 0
            self.count = 0
        return bytes(self.output)


def lzw_bomb(target):
    """An LZW stream decoding to about target bytes of 'a': after each clear, every code names
    the string one longer than the last (the decoder's KwKwK case)."""
    def width(code):
        # Each code is read when the decoder's next free entry equals it; PDF's default
        # EarlyChange 1 widens the code one entry early.
        return 12 if code + 1 >= 2048 else 11 if code + 1 >= 1024 else 10 if code + 1 >= 512 else 9

    bits = LzwBits()
    produced = 0
    while produced < target:
        bits.write(256, 9)
        bits.write(ord("a"), 9)
        produced += 1
        length = 1
        code = 258
        while code < 4094 and produced < target:
            bits.write(code, width(code))
            length += 1
            produced += length
            code += 1
        bits.write(256, 12)
    bits.write(257, 9)
    return bits.to_bytes()


def write_hostile(folder):
    os.makedirs(folder, exist_ok=True)
    names = []

    def save(name, data):
        with open(os.path.join(folder, name + ".pdf"), "wb") as handle:
            handle.write(data)
        names.append(name + ".pdf")
        print("  %s.pdf %d bytes" % (name, len(data)), file=sys.stderr)

    spaces_1g = deflate(b"", GIB, ord(" "))
    zeros_1g = deflate(b"", GIB, 0)

    save("valid-hello", spike_document(1, lambda page: "Hello hostile world, this is a valid page."))
    hundred = spike_document(100, lambda page: "Page %d of a valid document with a sentence on every page." % page)
    save("valid-100p", hundred)

    save("bomb-content-1g", single_page(lambda w: w.add_stream("/Filter /FlateDecode", spaces_1g), ""))
    save("bomb-content-3g-double-flate", single_page(lambda w: w.add_stream("/Filter [/FlateDecode /FlateDecode]", deflate(deflate(b"", 3 * GIB, ord(" ")))), ""))
    save("bomb-tounicode-1g", bomb_tounicode(spaces_1g))
    save("bomb-fontfile-1g", bomb_fontfile(zeros_1g))
    save("bomb-image-1g", bomb_image(zeros_1g))
    save("bomb-objstm-1g", bomb_object_stream())
    save("bomb-lzw-1g", single_page(lambda w: w.add_stream("/Filter /LZWDecode", lzw_bomb(GIB)), ""))
    save("bomb-predictor-columns", single_page(lambda w: w.add_stream("/Filter /FlateDecode /DecodeParms << /Predictor 12 /Colors 32 /BitsPerComponent 16 /Columns 2000000000 >>", deflate(latin1("BT /F1 12 Tf 72 720 Td (predictor) Tj ET"))), ""))

    save("deep-array-100k", single_page(lambda w: plain(w, "BT /F1 12 Tf 72 720 Td (deep) Tj ET"), "/Deep " + "[" * 100000 + "]" * 100000))
    save("deep-dict-100k", single_page(lambda w: plain(w, "BT /F1 12 Tf 72 720 Td (deep) Tj ET"), "/Deep " + "<< /A " * 100000 + ">> " * 100000))
    save("deep-content-array-100k", single_page(lambda w: plain(w, "BT /F1 12 Tf 72 720 Td " + "[" * 100000 + "]" * 100000 + " TJ (after) Tj ET"), ""))
    save("deep-q-1m", single_page(lambda w: w.add_stream("/Filter /FlateDecode", deflate(latin1("q " * 1000000 + "BT /F1 12 Tf 72 720 Td (saved) Tj ET"))), ""))

    save("cycle-self-contents", cycle_self_contents())
    save("cycle-page-tree", cycle_page_tree())
    save("cycle-parent-resources", cycle_parent_resources())
    save("cycle-form-self", form_cycle(pair=False))
    save("cycle-form-pair", form_cycle(pair=True))
    save("cycle-length-self", cycle_length_self())
    save("amplify-form-fanout-16x6", form_fanout(16, 6))

    save("pages-count-lie", pages_count_lie())
    save("pages-fanout-1m", pages_fanout(1000, 1000))
    save("pages-real-20k", spike_document(20000, lambda page: "p%d" % page))

    save("xref-shifted-offsets", rebuild3(lambda w, root: w.build(root, offset_shift=37)))
    save("xref-missing", rebuild3(lambda w, root: w.build(root, write_xref=False)))
    save("xref-startxref-garbage", rebuild3(lambda w, root: w.build(root, start_xref=999999999)))
    save("xref-stream-size-2g", xref_stream_huge_size())
    save("object-number-huge", object_number_huge())

    for percent in (10, 50, 90, 99):
        save("truncated-%02d" % percent, hundred[:len(hundred) * percent // 100])

    save("font-truetype-garbage-tables", font_truetype_garbage())
    save("font-cff-subr-recursion", font_cff_subr_recursion())
    save("font-type3-recursive", font_type3_recursive())
    save("content-5m-text-ops", single_page(lambda w: w.add_stream("/Filter /FlateDecode", deflate(latin1("BT /F1 12 Tf 72 720 Td " + "(a) Tj " * 5000000 + "ET"))), ""))
    save("content-inline-image-no-ei", single_page(lambda w: plain(w, "BT /F1 12 Tf 72 720 Td (before) Tj ET\nBI /W 50000 /H 50000 /BPC 8 /CS /RGB ID\n" + "Q" * (1 << 20)), ""))
    save("encrypted-unknown-password", encrypted_unknown_password())
    save("not-a-pdf", latin1("<html><body>not a pdf</body></html>\n" * 1000))
    save("empty-file", b"")
    return names
