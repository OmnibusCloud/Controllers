namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A string of the theory of strings: a sequence of code points. Lengths and positions count
/// code points, as the theory does.
/// </summary>
internal sealed class SmtStringValue : SmtValue
{
    #region Constants

    /// <summary>The empty string.</summary>
    public static readonly SmtStringValue EMPTY = new([]);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a string; it keeps the array it is given, which nobody changes afterwards.
    /// </summary>
    /// <param name="codePoints">The characters, as code points.</param>
    public SmtStringValue(int[] codePoints)
    {
        CodePoints = codePoints;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.String;

    /// <inheritdoc />
    public override long Weight => 1 + CodePoints.Length;

    /// <summary>The characters, as code points.</summary>
    public int[] CodePoints { get; }

    /// <summary>How many characters the string has.</summary>
    public int Length => CodePoints.Length;

    #endregion
}
