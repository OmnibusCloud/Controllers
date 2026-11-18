namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// One page of the generated crawl: what a site would serve at a URL, as blocks with their
/// roles. The same page is written twice, as a crawl keeps it: as HTML in a WARC response, and
/// as the text of that HTML in a WET conversion.
/// </summary>
public sealed class CrawlSynthPage
{
    #region Properties

    /// <summary>The page's URL, on a host of the reserved <c>.example</c> domain.</summary>
    public required string Url { get; init; }

    /// <summary>What kind of page it is (<see cref="CrawlSynth"/> names them): an article, a page of links, a page that repeats itself...</summary>
    public required string Kind { get; init; }

    /// <summary>The language the page is written in (ISO 639-3), or "" for a page without text.</summary>
    public required string Language { get; init; }

    /// <summary>The language label a crawl's classifier would give the page (one code, or several with commas), or null for none.</summary>
    public string? Label { get; init; }

    /// <summary>The page's title.</summary>
    public required string Title { get; init; }

    /// <summary>The page's blocks in reading order.</summary>
    public required IReadOnlyList<(CrawlSynthRole Role, string Text)> Blocks { get; init; }

    /// <summary>The charset the HTML is served in.</summary>
    public string Charset { get; init; } = "utf-8";

    /// <summary>Where the page states its charset: <c>header</c>, <c>meta</c>, <c>none</c>, or <c>references</c> for a page of ASCII with numeric character references.</summary>
    public string CharsetPlace { get; init; } = "header";

    /// <summary>Whether the HTML leaves its elements open, as much of the web does.</summary>
    public bool Unclosed { get; init; }

    #endregion
}
