using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The check of a value against the sort declared for the place it comes to: a constant's
/// value, an argument, a result, a field, an array's element. A model is only a witness when
/// every symbol is given a value of its own sort: an Int that the model sets to a half would
/// "satisfy" a problem that has no integer solution. The evaluator therefore never lets a
/// value pass such a place unchecked.
/// <para>
/// One widening is made, the one solvers make: an Int where a Real is declared becomes the
/// Real of the same number. Nothing else is converted.
/// </para>
/// <para>
/// Here are also the default values of the sorts: what a symbol the model left out is given.
/// </para>
/// </summary>
internal sealed class SmtConformance
{
    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly Dictionary<int, SmtValue> m_defaults = [];
    private Dictionary<int, SmtValue>? m_firstElements;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the checks of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    public SmtConformance(SmtRun run)
    {
        m_run = run;
        m_meter = run.Meter;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Checks a value against a sort.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="sort">The sort declared for it.</param>
    /// <param name="depth">The level of nesting the check is made at.</param>
    /// <returns>The value in that sort: itself, or the Real of an Int.</returns>
    /// <exception cref="SmtPartialException">The value is of another sort, or the evaluator has no values for the sort.</exception>
    public SmtValue Conform(SmtValue value, SmtSort sort, int depth)
    {
        switch (sort.Kind)
        {
            case SmtSortKind.Bool when value.Kind == SmtValueKind.Bool:
            case SmtSortKind.Int when value.Kind == SmtValueKind.Int:
            case SmtSortKind.Real when value.Kind == SmtValueKind.Real:
            case SmtSortKind.String when value.Kind == SmtValueKind.String:
                return value;
            case SmtSortKind.Real when value is SmtIntValue integer:
                return new SmtRealValue(new SmtRational(integer.Value));
            case SmtSortKind.BitVector when value is SmtBitVectorValue vector && vector.Width == sort.Width:
                return value;
            case SmtSortKind.Uninterpreted when value is SmtElementValue element && ReferenceEquals(element.Sort, sort):
                return value;
            case SmtSortKind.Datatype when value is SmtDatatypeValue built:
                ConformDatatype(built, sort, depth);
                return value;
            case SmtSortKind.Array when value is SmtArrayValue array:
                return ConformArray(array, sort, depth);
            case SmtSortKind.Unsupported:
                throw m_meter.Undecided(sort.Reason ?? SmtValidationReasons.UNSUPPORTED_SORT);
            default:
                throw m_meter.Doubt();
        }
    }

    /// <summary>
    /// The value a symbol of a sort is given when the model leaves it out. Any value of the
    /// sort would do; the same one is taken every time.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <param name="depth">The level of nesting the value is asked for at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The evaluator has no value of the sort.</exception>
    public SmtValue Default(SmtSort sort, int depth)
    {
        if (sort.Kind == SmtSortKind.Unsupported)
            throw m_meter.Undecided(sort.Reason ?? SmtValidationReasons.UNSUPPORTED_SORT);

        return TryDefault(sort, depth, null) ?? throw m_meter.Undecided(SmtValidationReasons.UNSUPPORTED_SORT);
    }

    /// <summary>
    /// A value inside another is of its sort exactly: nothing is widened there, since a solver
    /// that printed the outer value printed the inner one in its own sort.
    /// </summary>
    private void Exact(SmtValue value, SmtSort sort, int depth)
    {
        m_meter.Descend(depth);
        if (!ReferenceEquals(Conform(value, sort, depth), value))
            throw m_meter.Doubt();
    }

    private void ConformDatatype(SmtDatatypeValue value, SmtSort sort, int depth)
    {
        // Every field but the last by a call, the last by going round: a long list is checked
        // without a deep call stack. The values on the way are marked once all of it holds.
        List<(SmtDatatypeValue Value, SmtSort Sort)>? chain = null;
        while (!ReferenceEquals(value.ConformsTo, sort))
        {
            if (!ReferenceEquals(value.Constructor.Datatype, sort.Datatype))
                throw m_meter.Doubt();

            m_meter.Charge(1 + value.Fields.Length);
            var sorts = m_run.Problem.Resolver.FieldSorts(value.Constructor, sort, m_run.Sorts, m_meter);
            var last = value.Fields.Length - 1;
            for (var index = 0; index < last; index++)
                Exact(value.Fields[index], sorts[index], depth + 1);

            (chain ??= []).Add((value, sort));
            if (last < 0)
                break;

            if (value.Fields[last] is not SmtDatatypeValue next || sorts[last].Kind != SmtSortKind.Datatype)
            {
                Exact(value.Fields[last], sorts[last], depth + 1);
                break;
            }

            value = next;
            sort = sorts[last];
        }

        if (chain == null)
            return;

        foreach (var (checkedValue, checkedSort) in chain)
            checkedValue.ConformsTo = checkedSort;
    }

    private SmtValue ConformArray(SmtArrayValue array, SmtSort sort, int depth)
    {
        if (!ReferenceEquals(array.IndexSort, sort.Index))
            throw m_meter.Doubt();

        if (ReferenceEquals(array.ElementSort, sort.Element))
            return array;

        if (array.ElementSort != null)
            throw m_meter.Doubt();

        // The array came without the sort of its elements (a lambda states none): what it
        // stores is checked now, and what its function gives is checked when it is read.
        m_meter.Charge(1 + array.Entries.Count);
        foreach (var entry in array.Entries)
            Exact(entry.Value, sort.Element!, depth + 1);

        if (array.Default != null)
            Exact(array.Default, sort.Element!, depth + 1);

        return array.WithElementSort(sort.Element!);
    }

    /// <summary>
    /// The default value of a sort, or null when the sort has none the evaluator can build: a
    /// sort without values, or a datatype every constructor of which needs a value of the
    /// datatype itself (<paramref name="building"/> holds the datatypes on the way).
    /// </summary>
    private SmtValue? TryDefault(SmtSort sort, int depth, HashSet<int>? building)
    {
        if (m_defaults.TryGetValue(sort.Id, out var known))
            return known;

        m_meter.Descend(depth);
        m_meter.Charge(1);
        SmtValue? value;
        switch (sort.Kind)
        {
            case SmtSortKind.Bool:
                value = SmtBoolValue.FALSE;
                break;
            case SmtSortKind.Int:
                value = SmtIntValue.ZERO;
                break;
            case SmtSortKind.Real:
                value = new SmtRealValue(new SmtRational(BigInteger.Zero));
                break;
            case SmtSortKind.String:
                value = SmtStringValue.EMPTY;
                break;
            case SmtSortKind.BitVector:
                m_meter.CheckBitVectorBits(sort.Width);
                value = new SmtBitVectorValue(BigInteger.Zero, sort.Width);
                break;
            case SmtSortKind.Array:
                var element = TryDefault(sort.Element!, depth + 1, building);
                value = element == null ? null : new SmtArrayValue(sort.Index!, sort.Element, element, SmtArrayValue.NO_ENTRIES);
                break;
            case SmtSortKind.Uninterpreted:
                value = FirstElement(sort);
                break;
            case SmtSortKind.Datatype:
                value = DefaultOfDatatype(sort, depth, building ?? []);
                break;
            default:
                value = null;
                break;
        }

        // A datatype that failed on the way to itself may succeed from elsewhere: only a value is remembered.
        if (value != null)
            m_defaults[sort.Id] = value;

        return value;
    }

    private SmtValue? DefaultOfDatatype(SmtSort sort, int depth, HashSet<int> building)
    {
        if (!building.Add(sort.Id))
            return null;

        try
        {
            foreach (var constructor in sort.Datatype!.Constructors)
            {
                m_meter.Charge(1 + constructor.Arity);
                var sorts = m_run.Problem.Resolver.FieldSorts(constructor, sort, m_run.Sorts, m_meter);
                var fields = new SmtValue[sorts.Count];
                var built = true;
                for (var index = 0; index < fields.Length && built; index++)
                {
                    var field = TryDefault(sorts[index], depth + 1, building);
                    built = field != null;
                    if (built)
                        fields[index] = field!;
                }

                if (built)
                    return new SmtDatatypeValue(constructor, fields) { ConformsTo = sort };
            }

            return null;
        }
        finally
        {
            building.Remove(sort.Id);
        }
    }

    /// <summary>
    /// The default value of a sort the problem leaves to the model: the first element the model
    /// lists for it, or, when it lists none, an element of the evaluator's own.
    /// </summary>
    private SmtValue FirstElement(SmtSort sort)
    {
        if (m_firstElements == null)
        {
            m_firstElements = [];
            foreach (var declaration in m_run.Model.ElementsInOrder)
            {
                var declared = m_run.Resolve(declaration.Items[3]);
                if (declared.Kind == SmtSortKind.Uninterpreted)
                    m_firstElements.TryAdd(declared.Id, new SmtElementValue(declaration.Items[1].Text, declared));
            }
        }

        return m_firstElements.TryGetValue(sort.Id, out var first) ? first : new SmtElementValue("", sort);
    }

    #endregion
}
