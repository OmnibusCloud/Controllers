namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The local names of one activation: the parameters of a call, and on top of them the
/// variables of the <c>let</c> and <c>match</c> terms the evaluation is inside of. A name is
/// found in one lookup however deep the binders nest; a binder that ends takes its names back
/// off (<see cref="Unwind"/>), which restores what they hid.
/// </summary>
internal sealed class SmtScope
{
    #region Fields

    private readonly Dictionary<string, SmtValue> m_values;
    private List<(string Name, SmtValue? Hidden)>? m_bound;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a scope without names.
    /// </summary>
    /// <param name="isModel">Whether the terms evaluated in the scope are the model's text.</param>
    public SmtScope(bool isModel)
    {
        m_values = new Dictionary<string, SmtValue>(StringComparer.Ordinal);
        IsModel = isModel;
    }

    private SmtScope(SmtScope other)
    {
        m_values = new Dictionary<string, SmtValue>(other.m_values, StringComparer.Ordinal);
        IsModel = other.IsModel;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Gives a parameter its value, for as long as the scope lives.
    /// </summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">Its value.</param>
    public void Define(string name, SmtValue value)
    {
        m_values[name] = value;
    }

    /// <summary>
    /// Gives a variable of a binder its value, until the scope is unwound past this point.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="value">Its value.</param>
    public void Bind(string name, SmtValue value)
    {
        m_bound ??= [];
        m_bound.Add((name, m_values.TryGetValue(name, out var hidden) ? hidden : null));
        m_values[name] = value;
    }

    /// <summary>
    /// Takes back every variable bound since a mark, the last one first, and restores what
    /// each of them hid.
    /// </summary>
    /// <param name="mark">What <see cref="Mark"/> gave before the binders were entered.</param>
    public void Unwind(int mark)
    {
        if (m_bound == null)
            return;

        for (var index = m_bound.Count - 1; index >= mark; index--)
        {
            var (name, hidden) = m_bound[index];
            if (hidden == null)
                m_values.Remove(name);
            else
                m_values[name] = hidden;
        }

        if (m_bound.Count > mark)
            m_bound.RemoveRange(mark, m_bound.Count - mark);
    }

    /// <summary>
    /// Looks a name up.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">Its value when it is a local name.</param>
    /// <returns>True when the name is local.</returns>
    public bool TryGet(string name, out SmtValue value)
    {
        return m_values.TryGetValue(name, out value!);
    }

    /// <summary>
    /// Copies the names visible now into a scope of their own: what a <c>lambda</c> keeps of
    /// the place it was written in.
    /// </summary>
    /// <returns>The copy.</returns>
    public SmtScope Capture()
    {
        return new SmtScope(this);
    }

    #endregion

    #region Properties

    /// <summary>Whether the terms evaluated in the scope are the model's text, which may use the model's own names.</summary>
    public bool IsModel { get; }

    /// <summary>How many variables of binders are bound now: a point <see cref="Unwind"/> can return to.</summary>
    public int Mark => m_bound?.Count ?? 0;

    /// <summary>How many names are visible.</summary>
    public int Count => m_values.Count;

    #endregion
}
