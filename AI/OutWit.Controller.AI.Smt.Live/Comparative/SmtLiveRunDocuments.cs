using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;

namespace OutWit.Controller.AI.Smt.Live.Comparative;

/// <summary>The report, the integrity document and the provenance of a run's folder, parsed for reading; an unreadable one reads as empty.</summary>
internal sealed class SmtLiveRunDocuments : IDisposable
{
    #region Fields

    private readonly JsonDocument? m_report;
    private readonly JsonDocument? m_integrity;
    private readonly JsonDocument? m_provenance;

    #endregion

    #region Constructors

    private SmtLiveRunDocuments(JsonDocument? report, JsonDocument? integrity, JsonDocument? provenance)
    {
        m_report = report;
        m_integrity = integrity;
        m_provenance = provenance;
    }

    #endregion

    #region Functions

    /// <summary>Reads the three documents of a folder.</summary>
    /// <param name="folder">The folder a run wrote.</param>
    /// <returns>The documents.</returns>
    public static SmtLiveRunDocuments Read(string folder)
    {
        return new SmtLiveRunDocuments(Parse(folder, SmtLiveDocuments.REPORT), Parse(folder, SmtLiveDocuments.INTEGRITY), Parse(folder, SmtLiveDocuments.PROVENANCE));
    }

    private static JsonDocument? Parse(string folder, SmtLiveDocument document)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, document.File)));
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the parsed documents.</summary>
    public void Dispose()
    {
        m_report?.Dispose();
        m_integrity?.Dispose();
        m_provenance?.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The report's root; undefined when it could not be read.</summary>
    public JsonElement Report => m_report?.RootElement ?? default;

    /// <summary>The integrity document's root; undefined when it could not be read.</summary>
    public JsonElement Integrity => m_integrity?.RootElement ?? default;

    /// <summary>The provenance's root; undefined when it could not be read.</summary>
    public JsonElement Provenance => m_provenance?.RootElement ?? default;

    #endregion
}
