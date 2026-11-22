using System.Globalization;
using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The sorts of a problem, or of one validation on top of them: one object for each sort, so
/// that comparing two sorts is comparing two references, however the sorts were written and
/// however deep their aliases go.
/// <para>
/// A problem's table is filled once and then only read, by any number of threads. A validation
/// puts the sorts it meets in the model into a table of its own over the problem's.
/// </para>
/// </summary>
internal sealed class SmtSortTable
{
    #region Constants

    private const char SEPARATOR = ':';

    #endregion

    #region Fields

    private readonly SmtSortTable? m_parent;
    private readonly Dictionary<string, SmtSort> m_sorts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmtSort[]> m_memo = new(StringComparer.Ordinal);
    private readonly int m_firstId;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the table of a problem.
    /// </summary>
    public SmtSortTable()
    {
        Bool = Intern("B", id => new SmtSort(id, SmtSortKind.Bool));
        Int = Intern("I", id => new SmtSort(id, SmtSortKind.Int));
        Real = Intern("R", id => new SmtSort(id, SmtSortKind.Real));
        String = Intern("S", id => new SmtSort(id, SmtSortKind.String));
    }

    /// <summary>
    /// Creates the table of one validation over the table of its problem.
    /// </summary>
    /// <param name="parent">The problem's table, which is only read.</param>
    public SmtSortTable(SmtSortTable parent)
    {
        m_parent = parent;
        m_firstId = parent.Count;
        Bool = parent.Bool;
        Int = parent.Int;
        Real = parent.Real;
        String = parent.String;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The sort of bit-vectors of a width.
    /// </summary>
    /// <param name="width">The width, at least 1.</param>
    /// <returns>The sort.</returns>
    public SmtSort BitVector(int width)
    {
        return Intern("V" + width.ToString(CultureInfo.InvariantCulture), id => new SmtSort(id, SmtSortKind.BitVector) { Width = width });
    }

    /// <summary>
    /// The sort of arrays from an index sort to an element sort.
    /// </summary>
    /// <param name="index">The index sort.</param>
    /// <param name="element">The element sort.</param>
    /// <returns>The sort.</returns>
    public SmtSort Array(SmtSort index, SmtSort element)
    {
        return Intern(Key('A', 0, [index, element]), id => new SmtSort(id, SmtSortKind.Array) { Index = index, Element = element });
    }

    /// <summary>
    /// The sort of a datatype with its sort arguments.
    /// </summary>
    /// <param name="datatype">The declaration.</param>
    /// <param name="arguments">One sort for each parameter of the declaration.</param>
    /// <returns>The sort.</returns>
    public SmtSort Datatype(SmtDatatype datatype, IReadOnlyList<SmtSort> arguments)
    {
        return Intern(Key('D', datatype.Ordinal, arguments), id => new SmtSort(id, SmtSortKind.Datatype) { Datatype = datatype, Arguments = arguments });
    }

    /// <summary>
    /// A sort the problem declares and leaves to the model, with its sort arguments.
    /// </summary>
    /// <param name="declaration">The number of the declaration in the problem.</param>
    /// <param name="arguments">One sort for each parameter of the declaration.</param>
    /// <returns>The sort.</returns>
    public SmtSort Uninterpreted(int declaration, IReadOnlyList<SmtSort> arguments)
    {
        return Intern(Key('U', declaration, arguments), id => new SmtSort(id, SmtSortKind.Uninterpreted) { Declaration = declaration, Arguments = arguments });
    }

    /// <summary>
    /// The sort the evaluator has no values for, for one reason.
    /// </summary>
    /// <param name="reason">A code of <see cref="SmtValidationReasons"/>.</param>
    /// <returns>The sort.</returns>
    public SmtSort Unsupported(string reason)
    {
        return Intern("X" + reason, id => new SmtSort(id, SmtSortKind.Unsupported) { Reason = reason });
    }

    /// <summary>
    /// Looks up what was remembered under a key: an alias already expanded for its arguments,
    /// the field sorts of a constructor for one sort of its datatype.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="sorts">What was remembered.</param>
    /// <returns>True when something was.</returns>
    public bool TryRecall(string key, out SmtSort[] sorts)
    {
        for (var table = this; table != null; table = table.m_parent)
        {
            if (table.m_memo.TryGetValue(key, out sorts!))
                return true;
        }

        sorts = [];
        return false;
    }

    /// <summary>
    /// Remembers sorts under a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="sorts">The sorts.</param>
    public void Remember(string key, SmtSort[] sorts)
    {
        m_memo[key] = sorts;
    }

    /// <summary>
    /// Makes the key of a name applied to sorts.
    /// </summary>
    /// <param name="tag">What kind of name it is.</param>
    /// <param name="number">The name's number.</param>
    /// <param name="arguments">The sorts.</param>
    /// <returns>A key that two different applications never share.</returns>
    public static string Key(char tag, int number, IReadOnlyList<SmtSort> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(tag).Append(number.ToString(CultureInfo.InvariantCulture));
        foreach (var argument in arguments)
            builder.Append(SEPARATOR).Append(argument.Id.ToString(CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private SmtSort Intern(string key, Func<int, SmtSort> make)
    {
        for (var table = this; table != null; table = table.m_parent)
        {
            if (table.m_sorts.TryGetValue(key, out var known))
                return known;
        }

        var sort = make(Count);
        m_sorts[key] = sort;
        return sort;
    }

    #endregion

    #region Properties

    /// <summary>The sort Bool.</summary>
    public SmtSort Bool { get; }

    /// <summary>The sort Int.</summary>
    public SmtSort Int { get; }

    /// <summary>The sort Real.</summary>
    public SmtSort Real { get; }

    /// <summary>The sort String.</summary>
    public SmtSort String { get; }

    private int Count => m_firstId + m_sorts.Count;

    #endregion
}
