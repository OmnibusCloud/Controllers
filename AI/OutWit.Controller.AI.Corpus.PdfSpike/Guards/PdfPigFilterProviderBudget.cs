using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Guards;

/// <summary>
/// PdfPig's own filters, each behind the document's <see cref="PdfDecodeBudget"/>. Passed in
/// <c>ParsingOptions.FilterProvider</c>, it is the one road every stream decode of the library
/// takes: content streams, fonts, CMaps, object and cross-reference streams.
/// </summary>
public sealed class PdfPigFilterProviderBudget : IFilterProvider
{
    #region Fields

    private readonly IFilterProvider m_inner = DefaultFilterProvider.Instance;
    private readonly PdfDecodeBudget m_budget;

    #endregion

    #region Constructors

    /// <summary>Creates the provider for one document.</summary>
    /// <param name="budget">The document's budget.</param>
    public PdfPigFilterProviderBudget(PdfDecodeBudget budget)
    {
        m_budget = budget;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
    {
        return Wrap(m_inner.GetFilters(dictionary));
    }

    /// <inheritdoc />
    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
    {
        return Wrap(m_inner.GetNamedFilters(names));
    }

    /// <inheritdoc />
    public IReadOnlyList<IFilter> GetAllFilters()
    {
        return Wrap(m_inner.GetAllFilters());
    }

    private IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters)
    {
        return filters.Select(filter => (IFilter)new PdfPigFilterBudget(filter, m_budget)).ToArray();
    }

    #endregion
}
