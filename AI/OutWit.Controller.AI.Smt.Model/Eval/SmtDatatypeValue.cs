namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A value of an algebraic datatype: a constructor applied to the values of its fields.
/// </summary>
internal sealed class SmtDatatypeValue : SmtValue
{
    #region Fields

    private readonly int m_depth;
    private readonly long m_weight;
    private readonly long m_keyCost;
    private readonly bool m_holdsArray;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a datatype value; it keeps the array it is given, which nobody changes afterwards.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <param name="fields">One value for each field of the constructor.</param>
    public SmtDatatypeValue(SmtConstructor constructor, SmtValue[] fields)
    {
        Constructor = constructor;
        Fields = fields;

        var depth = 0;
        long weight = 1;
        long keyCost = 1;
        foreach (var field in fields)
        {
            depth = Math.Max(depth, field.Depth);
            weight = Sum(weight, field.Weight);
            keyCost = Sum(keyCost, field.KeyCost);
            m_holdsArray |= field.HoldsArray;
        }

        m_depth = depth == int.MaxValue ? int.MaxValue : depth + 1;
        m_weight = weight;
        m_keyCost = keyCost;
    }

    #endregion

    #region Functions

    private static long Sum(long left, long right)
    {
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.Datatype;

    /// <inheritdoc />
    public override int Depth => m_depth;

    /// <inheritdoc />
    public override long Weight => m_weight;

    /// <inheritdoc />
    public override long KeyCost => m_keyCost;

    /// <inheritdoc />
    public override bool HoldsArray => m_holdsArray;

    /// <summary>The constructor.</summary>
    public SmtConstructor Constructor { get; }

    /// <summary>The values of the fields, in the constructor's order.</summary>
    public SmtValue[] Fields { get; }

    /// <summary>
    /// The sort the value was last found to be of, fields and all: checking it against the
    /// same sort again costs nothing. A value lives in one validation, on one thread.
    /// </summary>
    public SmtSort? ConformsTo { get; set; }

    #endregion
}
