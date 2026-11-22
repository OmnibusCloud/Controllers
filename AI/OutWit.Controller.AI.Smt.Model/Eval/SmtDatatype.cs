namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// An algebraic datatype a problem declares: its name, its sort parameters and its
/// constructors in the order of the declaration.
/// </summary>
internal sealed class SmtDatatype
{
    #region Fields

    private readonly List<SmtConstructor> m_constructors = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a datatype without constructors; they are added as its declaration is read.
    /// </summary>
    /// <param name="name">The datatype's name.</param>
    /// <param name="ordinal">Its number among the datatypes of the problem.</param>
    /// <param name="parameters">The names of its sort parameters; none for a datatype that is one sort.</param>
    public SmtDatatype(string name, int ordinal, IReadOnlyList<string> parameters)
    {
        Name = name;
        Ordinal = ordinal;
        Parameters = parameters;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Adds a constructor, as the declaration is read.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    public void Add(SmtConstructor constructor)
    {
        m_constructors.Add(constructor);
    }

    #endregion

    #region Properties

    /// <summary>The datatype's name.</summary>
    public string Name { get; }

    /// <summary>The datatype's number among the datatypes of the problem.</summary>
    public int Ordinal { get; }

    /// <summary>The names of the sort parameters.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>The constructors, in the order of the declaration.</summary>
    public IReadOnlyList<SmtConstructor> Constructors => m_constructors;

    /// <summary>The one sort of a datatype without parameters; null for a datatype with parameters, and until the declaration is read.</summary>
    public SmtSort? Sort { get; set; }

    #endregion
}
