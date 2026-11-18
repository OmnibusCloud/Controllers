namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>What a block of a generated page is: where it goes in the HTML, and whether it is the page's own text or its furniture.</summary>
public enum CrawlSynthRole
{
    /// <summary>An entry of the site's menu.</summary>
    Menu,

    /// <summary>The heading of the page's body.</summary>
    Heading,

    /// <summary>A paragraph of the page's text.</summary>
    Paragraph,

    /// <summary>An item of a list in the page's body.</summary>
    Item,

    /// <summary>A link to another page, beside the text.</summary>
    Teaser,

    /// <summary>A line of the page's foot.</summary>
    Footer
}
