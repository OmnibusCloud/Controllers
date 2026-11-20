using System.Globalization;
using System.Text;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Warc;

/// <summary>
/// The two numbers a shard states for itself, a WARC record's <c>Content-Length</c> and an HTTP
/// response's status code, are read as unsigned decimal digits on every node. They were read with
/// the node's culture: a signed form (<c>+5</c>, <c>+200</c>) was read on most nodes and refused on
/// a node whose culture writes its plus sign with a direction mark (Arabic, Persian, Hebrew,
/// Pashto), so the same bytes made a shard read on one node and malformed on another.
/// </summary>
[TestFixture]
public sealed class WarcNumbersCultureTests
{
    #region Constants

    private static readonly string[] CULTURES = ["", "en-US", "de-DE", "tr-TR", "th-TH", "sv-SE", "ar-SA", "fa-IR", "he-IL", "ps-AF"];

    #endregion

    #region Tests

    [TestCaseSource(nameof(CULTURES))]
    public void ASignedContentLengthIsMalformedOnEveryNodeTest(string culture)
    {
        InCulture(culture, () =>
        {
            using var reader = new WarcReader(Record("+5", "hello"));
            var exception = Assert.Throws<WarcFormatException>(() => reader.ReadRecords().ToList());

            Assert.That(exception!.Message, Does.Contain("Content-Length"));
        });
    }

    [TestCaseSource(nameof(CULTURES))]
    public void AContentLengthOfDigitsIsReadOnEveryNodeTest(string culture)
    {
        InCulture(culture, () =>
        {
            using var reader = new WarcReader(Record("5", "hello"));
            var records = reader.ReadRecords().ToList();

            Assert.Multiple(() =>
            {
                Assert.That(records, Has.Count.EqualTo(1));
                Assert.That(Encoding.ASCII.GetString(records[0].Payload.Span), Is.EqualTo("hello"));
            });
        });
    }

    [TestCaseSource(nameof(CULTURES))]
    public void ASignedStatusIsNoStatusOnEveryNodeTest(string culture)
    {
        InCulture(culture, () =>
        {
            var signed = HttpResponsePayload.Parse(Response("HTTP/1.1 +200 OK"));
            var plain = HttpResponsePayload.Parse(Response("HTTP/1.1 200 OK"));

            Assert.Multiple(() =>
            {
                Assert.That(signed!.StatusCode, Is.Zero, "a signed status is no status: the page is dropped as http_status everywhere");
                Assert.That(plain!.StatusCode, Is.EqualTo(200));
            });
        });
    }

    #endregion

    #region Tools

    /// <summary>Runs a check under a culture; a culture this machine does not have is skipped.</summary>
    private static void InCulture(string name, Action check)
    {
        CultureInfo culture;
        try
        {
            culture = name.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            Assert.Ignore($"this machine has no culture '{name}'");
            return;
        }

        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            check();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static MemoryStream Record(string contentLength, string payload)
    {
        var text = $"WARC/1.0\r\nWARC-Type: conversion\r\nWARC-Target-URI: http://a/\r\nContent-Type: text/plain\r\nContent-Length: {contentLength}\r\n\r\n{payload}\r\n\r\n";
        return new MemoryStream(Encoding.ASCII.GetBytes(text));
    }

    private static ReadOnlyMemory<byte> Response(string statusLine)
    {
        return Encoding.ASCII.GetBytes($"{statusLine}\r\nContent-Type: text/html\r\n\r\n<html><body>page</body></html>");
    }

    #endregion
}
