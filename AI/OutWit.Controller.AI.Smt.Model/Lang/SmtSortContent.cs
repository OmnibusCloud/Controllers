namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// What the values of one sort hold, as far as <see cref="SmtUninterpretedSorts"/> asks: whether
/// they hold elements of an uninterpreted sort whatever the sort's arguments are, and for each
/// parameter of the sort whether they hold elements of the argument that takes its place. One
/// stands for each alias (<c>define-sort</c>) and each datatype of a problem, and one for the
/// sort of each constant that is asked about.
/// <para>
/// Both only ever grow: nothing is taken back once it is known. The datatypes of one declaration
/// may name each other, so what one of them holds can become known after it was asked: while
/// such a declaration is read its datatypes are open, and whoever asked one of them is kept
/// waiting and told when it turns out.
/// </para>
/// </summary>
internal sealed class SmtSortContent
{
    #region Constants

    private static readonly IReadOnlyList<SmtSortContent> NOBODY = [];

    private static readonly IReadOnlyList<(SmtSortContent Owner, SmtExpression Sort)> NOTHING = [];

    #endregion

    #region Fields

    private readonly Dictionary<string, int>? m_parameters;

    private readonly bool[] m_passed;

    private List<SmtSortContent>? m_waiting;

    private List<(SmtSortContent Owner, SmtExpression Sort)>?[]? m_held;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the content of a sort of which nothing is known yet.
    /// </summary>
    /// <param name="parameters">The sort's parameters, as their declaration names them, in order; none for a sort without.</param>
    /// <param name="isOpen">Whether the sort is a datatype of a declaration that is still being read.</param>
    public SmtSortContent(IReadOnlyList<SmtExpression> parameters, bool isOpen)
    {
        m_passed = new bool[parameters.Count];
        IsOpen = isOpen;

        if (parameters.Count == 0)
            return;

        // A name given to two parameters is the first one's, as a name declared twice is.
        m_parameters = new Dictionary<string, int>(parameters.Count, StringComparer.Ordinal);
        for (var index = 0; index < parameters.Count; index++)
            m_parameters.TryAdd(parameters[index].Text, index);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Finds a parameter of the sort by its name.
    /// </summary>
    /// <param name="name">A name.</param>
    /// <param name="parameter">The parameter's place, from 0.</param>
    /// <returns>True when the sort has a parameter of that name.</returns>
    public bool TryParameter(string name, out int parameter)
    {
        parameter = -1;
        return m_parameters != null && m_parameters.TryGetValue(name, out parameter);
    }

    /// <summary>
    /// Whether the sort's values hold elements of the argument that stands for a parameter, as
    /// far as is known now.
    /// </summary>
    /// <param name="parameter">The parameter's place, from 0; one past the sort's parameters is one it does not have.</param>
    /// <returns>True when they do.</returns>
    public bool Passes(int parameter)
    {
        return parameter < m_passed.Length && m_passed[parameter];
    }

    /// <summary>
    /// Notes that the sort's values hold elements of an uninterpreted sort.
    /// </summary>
    /// <param name="sort">The uninterpreted sort's name.</param>
    /// <returns>The contents that waited to hear of it: their values hold the sort's, so they hold that sort too. None when it was known already.</returns>
    public IReadOnlyList<SmtSortContent> Hold(string sort)
    {
        if (Uninterpreted != null)
            return NOBODY;

        Uninterpreted = sort;
        var waiting = m_waiting ?? NOBODY;
        m_waiting = null;
        return waiting;
    }

    /// <summary>
    /// Notes that the sort's values hold elements of the argument that stands for a parameter.
    /// </summary>
    /// <param name="parameter">The parameter's place, from 0.</param>
    /// <returns>The arguments that were given for the parameter while it was not known to be passed, each with the content it is to be read for. None when it was known already.</returns>
    public IReadOnlyList<(SmtSortContent Owner, SmtExpression Sort)> Pass(int parameter)
    {
        if (m_passed[parameter])
            return NOTHING;

        m_passed[parameter] = true;
        var held = m_held?[parameter] ?? NOTHING;
        if (m_held != null)
            m_held[parameter] = null;

        return held;
    }

    /// <summary>
    /// Keeps a content waiting for what this sort's values turn out to hold. Only an open sort
    /// keeps anybody waiting: of any other, everything is known.
    /// </summary>
    /// <param name="content">The content whose values hold this sort's.</param>
    public void Await(SmtSortContent content)
    {
        if (IsOpen && Uninterpreted == null)
            (m_waiting ??= []).Add(content);
    }

    /// <summary>
    /// Keeps an argument that was given for a parameter until the parameter turns out to be
    /// passed. Only an open sort keeps anything.
    /// </summary>
    /// <param name="parameter">The parameter's place, from 0.</param>
    /// <param name="owner">The content the argument is to be read for.</param>
    /// <param name="sort">The argument, as written.</param>
    public void Await(int parameter, SmtSortContent owner, SmtExpression sort)
    {
        if (!IsOpen || parameter >= m_passed.Length || m_passed[parameter])
            return;

        m_held ??= new List<(SmtSortContent Owner, SmtExpression Sort)>?[m_passed.Length];
        (m_held[parameter] ??= []).Add((owner, sort));
    }

    /// <summary>
    /// Ends the reading of the sort's declaration: what is known of it now is all there is, and
    /// whatever still waited is let go.
    /// </summary>
    public void Close()
    {
        IsOpen = false;
        m_waiting = null;
        m_held = null;
    }

    #endregion

    #region Properties

    /// <summary>The name of an uninterpreted sort whose elements the sort's values hold whatever its arguments; null when none is known.</summary>
    public string? Uninterpreted { get; private set; }

    /// <summary>Whether the sort's declaration is still being read.</summary>
    public bool IsOpen { get; private set; }

    #endregion
}
