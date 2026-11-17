using System.Diagnostics;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace OutWit.Controller.AI.Corpus.Tests.Text;

/// <summary>
/// What the HTML parser pays for hostile markup and what its own callbacks see of it (audit
/// finding H-6): how many elements it creates, how deep its tree goes, how long it takes — with
/// and without a stop at the nesting limit. A diagnostic: it prints, it asserts nothing, and it
/// runs only when asked for (<c>--filter Category=Diagnostics</c>).
/// </summary>
[TestFixture]
[Explicit("Diagnostics: prints what the HTML parser pays for hostile markup.")]
[Category("Diagnostics")]
public sealed class HtmlParserBudgetDiagnosticsTests
{
    private static string Repeat(string unit, int count)
    {
        return string.Concat(Enumerable.Repeat(unit, count));
    }

    private static IEnumerable<(string Name, string Html)> Pages()
    {
        yield return ("plain nesting 20k", "<html><body>" + Repeat("<div>", 20_000) + "text");
        yield return ("comment trick 20k", "<html><body><!-->" + Repeat("<div>", 20_000) + "text-->");
        yield return ("unmatched end tags 30k", "<html><body>" + Repeat("<div></x>", 30_000) + "text");
        yield return ("div table 20k", "<html><body>" + Repeat("<div><table></div>", 20_000) + "text");
        yield return ("formatting 400 x 5000", "<html><body><p>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<p>x", 5_000));
        yield return ("unclosed li 30k", "<html><body><ul>" + Repeat("<li>item", 30_000) + "</ul>");
        yield return ("flat p 30k", "<html><body>" + Repeat("<p>text</p>", 30_000));
        yield return ("table 100x300", "<html><body><table>" + Repeat("<tr>" + Repeat("<td>c", 100), 300) + "</table>");
        yield return ("formatting via br", "<html><body><p>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<p><br>x", 5_000));
        yield return ("formatting in tables", "<html><body>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<table>x</table>", 5_000));
        yield return ("formatting by end tags", "<html><body><div>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + "x" + string.Concat(Enumerable.Range(0, 400).Select(i => "</b>y")) + Repeat("<p>z", 2_000));
        yield return ("formatting in svg style", "<html><body><svg><style>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<p>x", 5_000));
        yield return ("flat p 60k tags", "<html><body>" + Repeat("<p>text</p>", 29_990));
        yield return ("comments 20k then p", "<html><body><div><p>x</p>" + Repeat("<!--c-->", 20_000) + Repeat("<p>y</p>", 5_000));
        yield return ("wide 29k span in one p", "<html><body><p>" + Repeat("<span>a</span> ", 29_000));
        yield return ("formatting 400, closed p", "<html><body><p>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<p>x</p>", 5_000));
        yield return ("formatting 400, closed div", "<html><body><p>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<div>x</div>", 5_000));
        yield return ("formatting 400, br in p", "<html><body><p>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<b a={i}>")) + Repeat("<p><br>x</p>", 5_000));
        yield return ("formatting grows per p", "<html><body>" + string.Concat(Enumerable.Range(0, 480).Select(i => $"<p><b a={i}>x</p>")));
        yield return ("one tag, 300k attributes", "<html><body><p " + string.Concat(Enumerable.Range(0, 300_000).Select(i => $"a{i} ")) + ">x</p>");
        yield return ("one tag, 300k same attr", "<html><body><p " + Repeat("a ", 300_000) + ">x</p>");
        yield return ("foster-parented br 58k", "<html><body><table>" + Repeat("<br>", 58_000) + "</table>");
        yield return ("foster-parented text 300k", "<html><body><table>" + Repeat("a<!--c-->", 300_000) + "</table>");
        yield return ("text and comments 300k", "<html><body><p>" + Repeat("a<!--c-->", 300_000));
        yield return ("references 500k", "<html><body><p>" + Repeat("&amp;", 500_000));
        yield return ("references unknown 300k", "<html><body><p>" + Repeat("&notanentity ", 300_000));
        yield return ("hidden by lt-digit", "<html><body>" + Repeat("<3<div>", 20_000) + "text");
        yield return ("select options 29k", "<html><body><select>" + Repeat("<option>o", 29_000));
        yield return ("nested tables to the limit", "<html><body>" + Repeat("<table><tr><td>", 100) + "x");
        yield return ("58k void in one p", "<html><body><p>" + Repeat("<img>", 58_000));
        yield return ("attrs 10 on 5k tags", "<html><body>" + Repeat("<p a=1 b=2 c=3 d=4 e=5 f=6 g=7 h=8 i=9 j=10>x</p>", 5_000));
        yield return ("4 MB of text", "<html><body><p>" + Repeat("word ", 800_000));
        yield return ("4 MB in a title", "<html><head><title>" + Repeat("word ", 800_000));
    }

    [Test]
    public void WhatTheParserPaysTest()
    {
        foreach (var (name, html) in Pages())
        {
            foreach (var stop in new[] { false, true })
            {
                var created = 0;
                var maxDepth = 0;
                var unattached = 0;
                IElement? previous = null;
                var options = new HtmlParserOptions
                {
                    IsScripting = false,
                    OnCreated = (element, _) =>
                    {
                        created++;
                        if (element.ParentElement == null && element.LocalName != "html")
                            unattached++;

                        // The element made before this one is in the tree by now.
                        var depth = Depth(previous, 600);
                        maxDepth = Math.Max(maxDepth, depth);
                        previous = element;
                        if (stop && (depth >= 512 || created > 240_000))
                            throw new InvalidOperationException("budget");
                    }
                };

                var clock = Stopwatch.StartNew();
                string outcome;
                try
                {
                    using var document = new HtmlParser(options).ParseDocument(html);
                    outcome = $"parsed, {document.All.Length} elements in the tree";
                }
                catch (InvalidOperationException exception) when (exception.Message == "budget")
                {
                    outcome = "stopped";
                }

                TestContext.Out.WriteLine($"H6| {name,-24} stop={stop,-5}: {outcome}; created {created}, unattached at creation {unattached}, deepest {maxDepth}, {clock.Elapsed.TotalSeconds:F2} s ({html.Length:N0} chars)");
            }

            // The extractor itself, with its budget on the parser's tree.
            var extractor = Stopwatch.StartNew();
            var reading = OutWit.Controller.AI.Corpus.Text.HtmlTextExtractor.Read(html, out var text);
            TestContext.Out.WriteLine($"H6| {name,-24} extractor: {reading}, {text.Length:N0} chars of text, {extractor.Elapsed.TotalSeconds:F2} s");
        }
    }

    private static int Depth(IElement? element, int most)
    {
        var depth = 0;
        for (var current = element; current != null && depth < most; current = current.ParentElement)
            depth++;

        return depth;
    }
}
