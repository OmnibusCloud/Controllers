#!/usr/bin/env python3
"""The PDF writer the test sets are made with: objects written byte by byte, as told.

Imported by pdfs.py and hostile_pdfs.py. Standard library only, Python 3.9 or later.
"""

import zlib


GIB = 1 << 30
HELVETICA = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"
PAGE_BOX = "/MediaBox [0 0 612 792]"


def latin1(text):
    return text.encode("latin-1")


class RawWriter:
    """Writes a PDF object by object, as told, including what a sane writer would refuse:
    wrong cross-reference offsets or none, a lying trailer, an absurd cross-reference stream.
    Object numbers are given in order from 1."""

    def __init__(self):
        self.objects = []

    def reserve(self):
        self.objects.append(None)
        return len(self.objects)

    def add(self, body):
        self.objects.append(latin1(body) if isinstance(body, str) else body)
        return len(self.objects)

    def set(self, number, body):
        self.objects[number - 1] = latin1(body) if isinstance(body, str) else body

    def add_stream(self, dictionary, data):
        self.objects.append(stream(dictionary, data))
        return len(self.objects)

    def set_stream(self, number, dictionary, data):
        self.objects[number - 1] = stream(dictionary, data)

    def _write_objects(self, output):
        output += latin1("%PDF-1.7\n%âãÏÓ\n")
        offsets = []
        for index, body in enumerate(self.objects):
            offsets.append(len(output))
            if body is None:
                continue
            output += latin1("%d 0 obj\n" % (index + 1))
            output += body
            output += latin1("\nendobj\n")
        return offsets

    def build(self, root, offset_shift=0, write_xref=True, start_xref=None, trailer_extra=""):
        """The file with a classic cross-reference table (offset_shift is added to every
        offset; write_xref=False writes no table and no trailer; start_xref overrides the
        table's offset)."""
        output = bytearray()
        offsets = self._write_objects(output)
        if not write_xref:
            output += latin1("%%EOF\n")
            return bytes(output)

        xref = len(output)
        table = ["xref\n0 %d\n0000000000 65535 f \n" % (len(self.objects) + 1)]
        for offset in offsets:
            table.append("%010d 00000 n \n" % (offset + offset_shift))
        table.append("trailer\n<< /Size %d /Root %d 0 R %s>>\nstartxref\n%d\n%%%%EOF\n" % (
            len(self.objects) + 1, root, trailer_extra, xref if start_xref is None else start_xref))
        output += latin1("".join(table))
        return bytes(output)

    def build_with_xref_stream(self, root, compressed, declared_size=None):
        """The file with a cross-reference stream (PDF 1.5) indexing the objects written here
        and, as type 2 entries, the (number, object stream, index) triples of compressed."""
        output = bytearray()
        offsets = self._write_objects(output)
        xref_number = len(self.objects) + 1
        size = xref_number + 1
        rows = bytearray(size * 7)
        rows[5:7] = (65535).to_bytes(2, "big")
        packed = {number: (stream_number, index) for number, stream_number, index in compressed}
        for number in range(1, size):
            row = number * 7
            if number == xref_number:
                rows[row] = 1
                rows[row + 1:row + 5] = len(output).to_bytes(4, "big")
            elif number in packed:
                rows[row] = 2
                rows[row + 1:row + 5] = packed[number][0].to_bytes(4, "big")
                rows[row + 5:row + 7] = packed[number][1].to_bytes(2, "big")
            elif self.objects[number - 1] is not None:
                rows[row] = 1
                rows[row + 1:row + 5] = offsets[number - 1].to_bytes(4, "big")

        xref = len(output)
        declared = size if declared_size is None else declared_size
        output += latin1("%d 0 obj\n<< /Type /XRef /Size %d /Index [0 %d] /W [1 4 2] /Root %d 0 R /Length %d >>\nstream\n" % (
            xref_number, declared, declared, root, len(rows)))
        output += rows
        output += latin1("\nendstream\nendobj\nstartxref\n%d\n%%%%EOF\n" % xref)
        return bytes(output)


def stream(dictionary, data):
    return latin1("<< %s /Length %d >>\nstream\n" % (dictionary, len(data))) + data + latin1("\nendstream")


def deflate(data=b"", padding=0, fill=0):
    """zlib (level 6, as .NET's CompressionLevel.Optimal) of data followed by padding bytes of fill."""
    compressor = zlib.compressobj(6)
    parts = [compressor.compress(data)]
    chunk = bytes([fill]) * (1 << 20)
    written = 0
    while written < padding:
        count = min(len(chunk), padding - written)
        parts.append(compressor.compress(chunk[:count]))
        written += count
    parts.append(compressor.flush())
    return b"".join(parts)


def page_dict(tree, resources, content):
    return "<< /Type /Page /Parent %d 0 R %s /Resources %s /Contents %d 0 R >>" % (tree, PAGE_BOX, resources, content)


def font_resources(font):
    return "<< /Font << /F1 %d 0 R >> >>" % font


def finish(writer, catalog, tree, kids, count):
    writer.set(catalog, "<< /Type /Catalog /Pages %d 0 R >>" % tree)
    writer.set(tree, "<< /Type /Pages /Kids [%s] /Count %d >>" % (kids, count))
    return writer.build(catalog)


def text_content(text, x=72, y=720):
    return latin1("BT /F1 12 Tf %d %d Td (%s) Tj ET" % (x, y, text))
