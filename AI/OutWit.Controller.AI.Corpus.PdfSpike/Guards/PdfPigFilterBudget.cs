using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Guards;

/// <summary>
/// One of PdfPig's filters behind a <see cref="PdfDecodeBudget"/>. A Flate stream is first
/// inflated into nothing, counting, and handed to the library's filter only when its size fits
/// the budget, so a bomb is refused before a byte of it is kept (the cost: Flate is inflated
/// twice). Predictor rows wider than a sane image are refused before the library sizes its row
/// buffer from them. Every other filter decodes as the library does and is then charged.
/// </summary>
public sealed class PdfPigFilterBudget : IFilter
{
    #region Constants

    private const int MAX_PREDICTOR_COLUMNS = 1 << 20;
    private const int COUNT_BUFFER_BYTES = 1 << 16;

    #endregion

    #region Fields

    private readonly IFilter m_inner;
    private readonly PdfDecodeBudget m_budget;

    #endregion

    #region Constructors

    /// <summary>Wraps a filter.</summary>
    /// <param name="inner">The library's filter.</param>
    /// <param name="budget">The document's budget.</param>
    public PdfPigFilterBudget(IFilter inner, PdfDecodeBudget budget)
    {
        m_inner = inner;
        m_budget = budget;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        m_budget.CheckDeadline();

        if (m_inner is FlateFilter)
        {
            CheckPredictorColumns(streamDictionary);
            m_budget.Spend(InflatedLength(input));
            return m_inner.Decode(input, streamDictionary, filterProvider, filterIndex);
        }

        var output = m_inner.Decode(input, streamDictionary, filterProvider, filterIndex);
        m_budget.Spend(output.Length);
        return output;
    }

    private long InflatedLength(Memory<byte> input)
    {
        if (input.Length < 2)
            return input.Length;

        var limit = m_budget.MaxStreamBytes + 1;
        long total = 0;
        var buffer = new byte[COUNT_BUFFER_BYTES];

        try
        {
            using var source = new MemoryStream(input[2..].ToArray(), writable: false);
            using var inflater = new DeflateStream(source, CompressionMode.Decompress);
            int read;
            while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total >= limit)
                    break;
            }
        }
        catch (InvalidDataException)
        {
            // The library keeps what inflated before the damage, or the raw input; either is at most what was counted.
            total = Math.Max(total, input.Length);
        }

        return total;
    }

    private static void CheckPredictorColumns(DictionaryToken streamDictionary)
    {
        if (!streamDictionary.TryGet(NameToken.DecodeParms, out var parameters) && !streamDictionary.TryGet(NameToken.Dp, out parameters))
            return;

        var dictionaries = parameters switch
        {
            DictionaryToken one => [one],
            ArrayToken many => many.Data.OfType<DictionaryToken>().ToArray(),
            _ => Array.Empty<DictionaryToken>(),
        };

        foreach (var dictionary in dictionaries)
        {
            if (dictionary.TryGet(NameToken.Columns, out var token) && token is NumericToken columns && (columns.Double > MAX_PREDICTOR_COLUMNS || columns.Double < 0))
                throw new PdfBudgetExceededException($"predictor columns {columns.Double} past {MAX_PREDICTOR_COLUMNS}");
        }
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public bool IsSupported => m_inner.IsSupported;

    #endregion
}
