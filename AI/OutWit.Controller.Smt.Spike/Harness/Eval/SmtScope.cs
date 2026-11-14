namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>Local names of a term (let bindings, function parameters, match variables), innermost first.</summary>
internal sealed class SmtScope
{
    #region Fields

    private readonly Dictionary<string, SmtValue> m_names;
    private readonly SmtScope? m_parent;

    #endregion

    #region Constructors

    /// <summary>Makes a scope over a parent.</summary>
    /// <param name="names">The names this scope binds.</param>
    /// <param name="parent">The enclosing scope, or null.</param>
    public SmtScope(Dictionary<string, SmtValue> names, SmtScope? parent)
    {
        m_names = names;
        m_parent = parent;
    }

    #endregion

    #region Functions

    /// <summary>Looks a name up, innermost scope first.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">Its value when bound.</param>
    /// <returns>True when bound.</returns>
    public bool TryGet(string name, out SmtValue value)
    {
        for (var scope = this; scope != null; scope = scope.m_parent)
        {
            if (scope.m_names.TryGetValue(name, out value!))
                return true;
        }

        value = null!;
        return false;
    }

    #endregion
}
