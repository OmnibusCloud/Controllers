using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The record ids of one generated shard: derived from the shard's name and a count, so that a
/// shard is the same bytes whenever and wherever it is generated (a crawler writes random ones).
/// </summary>
internal sealed class CrawlSynthIds
{
    #region Fields

    private readonly string m_shard;

    private int m_count;

    #endregion

    #region Constructors

    /// <summary>Starts the ids of a shard.</summary>
    /// <param name="shard">The shard's name: two shards do not share an id.</param>
    public CrawlSynthIds(string shard)
    {
        m_shard = shard;
    }

    #endregion

    #region Functions

    /// <summary>The next id, in the form a WARC header carries it.</summary>
    /// <returns>The id.</returns>
    public string Next()
    {
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes($"crawl-synth/{m_shard}/{m_count++}")));
        Last = $"<urn:uuid:{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}>";
        return Last;
    }

    #endregion

    #region Properties

    /// <summary>The id given last.</summary>
    public string Last { get; private set; } = "";

    #endregion
}
