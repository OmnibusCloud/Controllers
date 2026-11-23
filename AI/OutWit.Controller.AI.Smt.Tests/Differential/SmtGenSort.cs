namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// A sort of the generated terms, with its SMT-LIB text. Two sorts are the same sort when their
/// texts are the same.
/// </summary>
internal sealed class SmtGenSort
{
    #region Constants

    /// <summary>Bool.</summary>
    public static readonly SmtGenSort BOOL = new(SmtGenSortKind.Bool, "Bool");

    /// <summary>Int.</summary>
    public static readonly SmtGenSort INT = new(SmtGenSortKind.Int, "Int");

    /// <summary>Real.</summary>
    public static readonly SmtGenSort REAL = new(SmtGenSortKind.Real, "Real");

    /// <summary>String.</summary>
    public static readonly SmtGenSort STRING = new(SmtGenSortKind.String, "String");

    #endregion

    #region Constructors

    private SmtGenSort(SmtGenSortKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    #endregion

    #region Functions

    /// <summary>A bit-vector sort.</summary>
    /// <param name="width">The width, at least 1.</param>
    /// <returns>The sort.</returns>
    public static SmtGenSort BitVector(int width)
    {
        return new SmtGenSort(SmtGenSortKind.BitVector, $"(_ BitVec {width})") { Width = width };
    }

    /// <summary>An array sort.</summary>
    /// <param name="index">The index sort.</param>
    /// <param name="element">The element sort.</param>
    /// <returns>The sort.</returns>
    public static SmtGenSort Array(SmtGenSort index, SmtGenSort element)
    {
        return new SmtGenSort(SmtGenSortKind.Array, $"(Array {index.Text} {element.Text})") { Index = index, Element = element };
    }

    /// <summary>A datatype the prelude declares.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The sort.</returns>
    public static SmtGenSort Datatype(string name)
    {
        return new SmtGenSort(SmtGenSortKind.Datatype, name);
    }

    /// <summary>A sort the prelude declares and the model populates.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The sort.</returns>
    public static SmtGenSort Uninterpreted(string name)
    {
        return new SmtGenSort(SmtGenSortKind.Uninterpreted, name);
    }

    /// <summary>Whether another sort is the same sort.</summary>
    /// <param name="other">The other sort.</param>
    /// <returns>True for the same text.</returns>
    public bool Is(SmtGenSort other)
    {
        return string.Equals(Text, other.Text, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Text;
    }

    #endregion

    #region Properties

    /// <summary>What the sort is.</summary>
    public SmtGenSortKind Kind { get; }

    /// <summary>The sort as SMT-LIB writes it.</summary>
    public string Text { get; }

    /// <summary>A bit-vector's width; 0 for any other sort.</summary>
    public int Width { get; private init; }

    /// <summary>An array's index sort; null for any other sort.</summary>
    public SmtGenSort? Index { get; private init; }

    /// <summary>An array's element sort; null for any other sort.</summary>
    public SmtGenSort? Element { get; private init; }

    #endregion
}
