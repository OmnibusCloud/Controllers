using System.Collections.ObjectModel;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// What a reader has seen of a problem set so far: how many rows, how many taken and refused,
/// the taken problems by logic, by query and by the verdict they expect, what was stripped of
/// them, the refusals by code with the first of them in full, and the licences the problems
/// name. It is kept by the reader while it reads: look at it between two rows, or when the
/// set has ended.
/// <para>
/// Everything in it is bounded, whatever the set holds: at most
/// <see cref="SmtProblemSetLimits.MAX_REFUSALS_KEPT"/> refusals are kept in full, and at most
/// <see cref="SmtProblemSetLimits.MAX_LOGICS"/> logics and
/// <see cref="SmtProblemSetLimits.MAX_LICENSES"/> licences by name; the rest is counted.
/// </para>
/// </summary>
public sealed class SmtProblemSetSummary
{
    #region Fields

    private readonly Dictionary<string, int> m_logics = new(StringComparer.Ordinal);
    private readonly Dictionary<SmtQuery, int> m_queries = [];
    private readonly Dictionary<SmtProblemStatus, int> m_expected = [];
    private readonly Dictionary<string, int> m_stripped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_licenses = new(StringComparer.Ordinal);
    private readonly List<SmtRefusalData> m_refusals = [];

    #endregion

    #region Constructors

    internal SmtProblemSetSummary()
    {
        Logics = new ReadOnlyDictionary<string, int>(m_logics);
        Queries = new ReadOnlyDictionary<SmtQuery, int>(m_queries);
        Expected = new ReadOnlyDictionary<SmtProblemStatus, int>(m_expected);
        Stripped = new ReadOnlyDictionary<string, int>(m_stripped);
        RefusalCodes = new ReadOnlyDictionary<string, int>(m_codes);
        Licenses = new ReadOnlyDictionary<string, int>(m_licenses);
        Refusals = m_refusals.AsReadOnly();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Counts one row.
    /// </summary>
    /// <param name="row">The row the reader has just read.</param>
    internal void Add(SmtProblemSetRow row)
    {
        Rows++;

        if (row.IsAccepted)
        {
            Accepted++;
            Count(m_queries, row.Data.Query);
            Count(m_expected, row.Data.Expected);

            foreach (var (command, count) in row.Problem.Stripped)
                m_stripped[command] = m_stripped.GetValueOrDefault(command) + count;

            // A logic is a symbol of the lab's: it is shown as a token is, cut short.
            if (!CountByName(m_logics, SmtRefusals.Token(row.Problem.Logic ?? ""), SmtProblemSetLimits.MAX_LOGICS))
                OtherLogics++;

            if (!CountByName(m_licenses, row.Problem.License, SmtProblemSetLimits.MAX_LICENSES))
                OtherLicenses++;

            return;
        }

        Refused++;
        Count(m_codes, row.Refusal.Code);

        // A copy: the row's own record is the caller's to keep or to change.
        if (m_refusals.Count < SmtProblemSetLimits.MAX_REFUSALS_KEPT)
            m_refusals.Add(row.Refusal.Clone());
    }

    private static void Count<TKey>(Dictionary<TKey, int> counts, TKey key)
        where TKey : notnull
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    /// <summary>Counts under a name while there is room for the name; says whether it was counted.</summary>
    private static bool CountByName(Dictionary<string, int> counts, string name, int most)
    {
        if (!counts.ContainsKey(name) && counts.Count >= most)
            return false;

        Count(counts, name);
        return true;
    }

    #endregion

    #region Properties

    /// <summary>The rows read: taken and refused. Blank lines are no rows.</summary>
    public int Rows { get; private set; }

    /// <summary>The rows that were taken.</summary>
    public int Accepted { get; private set; }

    /// <summary>The rows that were refused.</summary>
    public int Refused { get; private set; }

    /// <summary>The bytes of the stream read so far: at the end, the size of the set.</summary>
    public long BytesRead { get; internal set; }

    /// <summary>
    /// The taken problems by the logic they name, the name cut to 64 characters; the empty name
    /// counts the problems that name none. At most <see cref="SmtProblemSetLimits.MAX_LOGICS"/>
    /// names (<see cref="OtherLogics"/> counts the rest).
    /// </summary>
    public IReadOnlyDictionary<string, int> Logics { get; }

    /// <summary>The taken problems whose logic is not among <see cref="Logics"/>: the set names more logics than are kept.</summary>
    public int OtherLogics { get; private set; }

    /// <summary>The taken problems by what is asked of them.</summary>
    public IReadOnlyDictionary<SmtQuery, int> Queries { get; }

    /// <summary>The taken problems by the verdict they expect: sat, unsat, or none.</summary>
    public IReadOnlyDictionary<SmtProblemStatus, int> Expected { get; }

    /// <summary>The commands that were stripped of the taken problems, by name.</summary>
    public IReadOnlyDictionary<string, int> Stripped { get; }

    /// <summary>The refused rows by the code of their refusal.</summary>
    public IReadOnlyDictionary<string, int> RefusalCodes { get; }

    /// <summary>
    /// The first refusals in full, in the order of the set: at most
    /// <see cref="SmtProblemSetLimits.MAX_REFUSALS_KEPT"/>. The refusals after them are counted
    /// (<see cref="Refused"/>, <see cref="RefusalCodes"/>) and not kept.
    /// </summary>
    public IReadOnlyList<SmtRefusalData> Refusals { get; }

    /// <summary>
    /// The taken problems by their <c>:license</c>, for an attribution list; the empty name
    /// counts the problems that name none. At most <see cref="SmtProblemSetLimits.MAX_LICENSES"/>
    /// names (<see cref="OtherLicenses"/> counts the rest).
    /// </summary>
    public IReadOnlyDictionary<string, int> Licenses { get; }

    /// <summary>The taken problems whose licence is not among <see cref="Licenses"/>: the set names more licences than are kept.</summary>
    public int OtherLicenses { get; private set; }

    #endregion
}
