namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Whether two values are the same value. Values of sorts that cannot be compared (a bit-vector
/// and an integer, bit-vectors of two widths, values of two datatypes) are no answer "no": the
/// term that compares them is one the evaluator cannot make sense of. An Int and a Real are
/// compared by their number, as solvers that mix the two sorts compare them.
/// </summary>
internal sealed class SmtEquality
{
    #region Constants

    /// <summary>Up to this many values are told apart pair by pair; more are sorted, where they can be.</summary>
    private const int PAIRWISE_VALUES = 8;

    #endregion

    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the equality of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    public SmtEquality(SmtRun run)
    {
        m_run = run;
        m_meter = run.Meter;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Whether two values are the same value.
    /// </summary>
    /// <param name="left">One value.</param>
    /// <param name="right">The other.</param>
    /// <param name="depth">The level of nesting the comparison is made at.</param>
    /// <returns>True for the same value.</returns>
    /// <exception cref="SmtPartialException">The values cannot be compared, or the comparison is not decided (two arrays).</exception>
    public bool Same(SmtValue left, SmtValue right, int depth)
    {
        m_meter.Descend(depth);
        while (true)
        {
            m_meter.Charge(1);
            if (left is not SmtDatatypeValue leftBuilt)
                return SameLeaf(left, right, depth);

            if (right is not SmtDatatypeValue rightBuilt || !ReferenceEquals(leftBuilt.Constructor.Datatype, rightBuilt.Constructor.Datatype))
                throw m_meter.Doubt();

            if (ReferenceEquals(left, right))
                return true;

            if (!ReferenceEquals(leftBuilt.Constructor, rightBuilt.Constructor))
                return false;

            // Every field but the last by a call, the last by going round: two long lists are
            // compared without a deep call stack.
            var last = leftBuilt.Fields.Length - 1;
            if (last < 0)
                return true;

            for (var index = 0; index < last; index++)
            {
                if (!Same(leftBuilt.Fields[index], rightBuilt.Fields[index], depth + 1))
                    return false;
            }

            left = leftBuilt.Fields[last];
            right = rightBuilt.Fields[last];
        }
    }

    /// <summary>
    /// Whether the values of a list all differ from each other.
    /// </summary>
    /// <param name="values">The values, two or more.</param>
    /// <param name="depth">The level of nesting the comparison is made at.</param>
    /// <returns>True when no two values are the same.</returns>
    /// <exception cref="SmtPartialException">The values cannot be compared, or a comparison is not decided.</exception>
    public bool Distinct(SmtValue[] values, int depth)
    {
        if (values.Length < 2)
            throw m_meter.Doubt();

        if (values.Length > PAIRWISE_VALUES && CanSort(values))
            return DistinctSorted(values);

        m_meter.Charge((long)values.Length * values.Length);
        for (var first = 0; first < values.Length; first++)
        {
            for (var second = first + 1; second < values.Length; second++)
            {
                if (Same(values[first], values[second], depth + 1))
                    return false;
            }
        }

        return true;
    }

    private bool SameLeaf(SmtValue left, SmtValue right, int depth)
    {
        switch (left.Kind)
        {
            case SmtValueKind.Bool when right is SmtBoolValue truth:
                return ((SmtBoolValue)left).Value == truth.Value;
            case SmtValueKind.Int when right.Kind is SmtValueKind.Int or SmtValueKind.Real:
            case SmtValueKind.Real when right.Kind is SmtValueKind.Int or SmtValueKind.Real:
                return SmtArithmetic.Compare(left, right, m_meter) == 0;
            case SmtValueKind.BitVector when right is SmtBitVectorValue vector && vector.Width == ((SmtBitVectorValue)left).Width:
                m_meter.Charge(left.Weight);
                return ((SmtBitVectorValue)left).Value == vector.Value;
            case SmtValueKind.String when right is SmtStringValue text:
                m_meter.Charge(Math.Min(left.Weight, right.Weight));
                return ((SmtStringValue)left).CodePoints.AsSpan().SequenceEqual(text.CodePoints);
            case SmtValueKind.Element when right is SmtElementValue element && ReferenceEquals(element.Sort, ((SmtElementValue)left).Sort):
                return SameName(((SmtElementValue)left).Name, element.Name);
            case SmtValueKind.Array when right is SmtArrayValue array:
                return m_run.Arrays.Same((SmtArrayValue)left, array, depth);
            default:
                throw m_meter.Doubt();
        }
    }

    /// <summary>
    /// Whether two elements have the same name. One name read twice is one text, and names of
    /// two lengths are told apart at once; two texts of one length are compared character by
    /// character, which is paid for by the length.
    /// </summary>
    private bool SameName(string left, string right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left.Length != right.Length)
            return false;

        m_meter.ChargeName(left);
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a list of values can be put in order: none holds an array, none nests deeper than
    /// the order follows, and all are of sorts that compare with the first one's.
    /// </summary>
    private bool CanSort(SmtValue[] values)
    {
        foreach (var value in values)
        {
            if (value.HoldsArray || value.Depth > SmtValueComparer.MAX_DEPTH)
                return false;

            if (!Comparable(values[0], value))
                throw m_meter.Doubt();
        }

        return true;
    }

    private bool DistinctSorted(SmtValue[] values)
    {
        long weight = 1;
        foreach (var value in values)
            weight = Math.Max(weight, value.KeyCost);

        // A sort of n values compares about n times the logarithm of n of them.
        var comparisons = (long)values.Length * (2 + (long)Math.Log2(values.Length));
        m_meter.Charge(weight > long.MaxValue / comparisons ? long.MaxValue : weight * comparisons);

        var sorted = (SmtValue[])values.Clone();
        Array.Sort(sorted, SmtValueComparer.INSTANCE);
        for (var index = 1; index < sorted.Length; index++)
        {
            if (SmtValueComparer.INSTANCE.Compare(sorted[index - 1], sorted[index]) == 0)
                return false;
        }

        return true;
    }

    private static bool Comparable(SmtValue left, SmtValue right)
    {
        switch (left.Kind)
        {
            case SmtValueKind.Int:
            case SmtValueKind.Real:
                return right.Kind is SmtValueKind.Int or SmtValueKind.Real;
            case SmtValueKind.BitVector:
                return right is SmtBitVectorValue vector && vector.Width == ((SmtBitVectorValue)left).Width;
            case SmtValueKind.Element:
                return right is SmtElementValue element && ReferenceEquals(element.Sort, ((SmtElementValue)left).Sort);
            case SmtValueKind.Datatype:
                return right is SmtDatatypeValue built && ReferenceEquals(built.Constructor.Datatype, ((SmtDatatypeValue)left).Constructor.Datatype);
            default:
                return left.Kind == right.Kind;
        }
    }

    #endregion
}
