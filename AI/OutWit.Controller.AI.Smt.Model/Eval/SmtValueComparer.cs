namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A total order of the values that can be indices of an array: every value that holds no
/// array. Two values compare equal exactly when they are the same value (an Int and a Real of
/// the same number are, as everywhere in the evaluator), so a sorted map keyed by this order is
/// a map keyed by value. The order depends on nothing but the values: no hash code, no machine,
/// no run.
/// </summary>
internal sealed class SmtValueComparer : IComparer<SmtValue>
{
    #region Constants

    /// <summary>The only comparer: it has no state.</summary>
    public static readonly SmtValueComparer INSTANCE = new();

    /// <summary>
    /// The deepest value that may be an index. The comparison recurses on the fields of a
    /// datatype value, unmetered inside the map, so the depth it may reach is fixed here.
    /// </summary>
    public const int MAX_DEPTH = 256;

    #endregion

    #region Constructors

    private SmtValueComparer()
    {
    }

    #endregion

    #region Functions

    /// <summary>
    /// Compares two values that hold no array.
    /// </summary>
    /// <param name="x">One value.</param>
    /// <param name="y">The other.</param>
    /// <returns>Negative, zero or positive, as <paramref name="x"/> is before, the same as, or after <paramref name="y"/>.</returns>
    /// <exception cref="InvalidOperationException">A value is an array or holds one: it is no index.</exception>
    public int Compare(SmtValue? x, SmtValue? y)
    {
        while (true)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            var rank = Rank(x).CompareTo(Rank(y));
            if (rank != 0)
                return rank;

            if (x is not SmtDatatypeValue left || y is not SmtDatatypeValue right)
                return CompareLeaves(x, y);

            var head = CompareConstructors(left.Constructor, right.Constructor);
            if (head != 0 || left.Fields.Length == 0)
                return head;

            // Every field but the last by a call, the last by going round: a long list is
            // compared without a deep call stack.
            var last = left.Fields.Length - 1;
            for (var index = 0; index < last; index++)
            {
                var field = Compare(left.Fields[index], right.Fields[index]);
                if (field != 0)
                    return field;
            }

            x = left.Fields[last];
            y = right.Fields[last];
        }
    }

    /// <summary>
    /// Compares two numbers, each an Int or a Real, by their value.
    /// </summary>
    /// <param name="left">One number.</param>
    /// <param name="right">The other.</param>
    /// <returns>Negative, zero or positive.</returns>
    public static int CompareNumbers(SmtValue left, SmtValue right)
    {
        if (left is SmtIntValue leftInt && right is SmtIntValue rightInt)
            return leftInt.Value.CompareTo(rightInt.Value);

        return Rational(left).CompareTo(Rational(right));
    }

    /// <summary>
    /// The rational of a number.
    /// </summary>
    /// <param name="number">An Int or a Real.</param>
    /// <returns>Its value.</returns>
    /// <exception cref="InvalidOperationException">The value is no number.</exception>
    public static SmtRational Rational(SmtValue number)
    {
        return number switch
        {
            SmtIntValue integer => new SmtRational(integer.Value),
            SmtRealValue real => real.Value,
            _ => throw new InvalidOperationException("the value is no number")
        };
    }

    private static int CompareLeaves(SmtValue x, SmtValue y)
    {
        switch (x.Kind)
        {
            case SmtValueKind.Bool:
                return ((SmtBoolValue)x).Value.CompareTo(((SmtBoolValue)y).Value);
            case SmtValueKind.Int:
            case SmtValueKind.Real:
                return CompareNumbers(x, y);
            case SmtValueKind.BitVector:
                var leftVector = (SmtBitVectorValue)x;
                var rightVector = (SmtBitVectorValue)y;
                var width = leftVector.Width.CompareTo(rightVector.Width);
                return width != 0 ? width : leftVector.Value.CompareTo(rightVector.Value);
            case SmtValueKind.String:
                return ((SmtStringValue)x).CodePoints.AsSpan().SequenceCompareTo(((SmtStringValue)y).CodePoints);
            case SmtValueKind.Element:
                var leftElement = (SmtElementValue)x;
                var rightElement = (SmtElementValue)y;
                var sort = leftElement.Sort.Id.CompareTo(rightElement.Sort.Id);
                return sort != 0 ? sort : string.CompareOrdinal(leftElement.Name, rightElement.Name);
            default:
                throw new InvalidOperationException("an array is no index of an array");
        }
    }

    private static int CompareConstructors(SmtConstructor left, SmtConstructor right)
    {
        var datatype = left.Datatype.Ordinal.CompareTo(right.Datatype.Ordinal);
        return datatype != 0 ? datatype : left.Ordinal.CompareTo(right.Ordinal);
    }

    /// <summary>
    /// An Int and a Real are one kind in the order: they compare by their number.
    /// </summary>
    private static int Rank(SmtValue value)
    {
        return value.Kind == SmtValueKind.Real ? (int)SmtValueKind.Int : (int)value.Kind;
    }

    #endregion
}
