namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What a source file holds. Append new formats at the end: the value travels on the wire.
/// </summary>
public enum CorpusSourceFormat
{
    /// <summary>Common Crawl WET: one plain-text conversion record per page.</summary>
    Wet = 0,

    /// <summary>WARC: HTTP request, response and metadata records; the text comes from the responses' HTML.</summary>
    Warc = 1
}
