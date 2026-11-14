namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>A value of an algebraic datatype: a constructor applied to field values.</summary>
internal sealed class SmtDatatypeValue : SmtValue
{
    #region Constructors

    /// <summary>Makes a datatype value.</summary>
    /// <param name="constructor">The constructor's name.</param>
    /// <param name="fields">The field values, in the constructor's order.</param>
    public SmtDatatypeValue(string constructor, IReadOnlyList<SmtValue> fields)
    {
        Constructor = constructor;
        Fields = fields;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        if (other is not SmtDatatypeValue d || d.Constructor != Constructor || d.Fields.Count != Fields.Count)
            return false;

        for (var i = 0; i < Fields.Count; i++)
        {
            if (!Fields[i].SameAs(d.Fields[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override string Key()
    {
        return Fields.Count == 0 ? $"c:{Constructor}" : $"c:({Constructor} {string.Join(" ", Fields.Select(f => f.Key()))})";
    }

    #endregion

    #region Properties

    /// <summary>The constructor's name.</summary>
    public string Constructor { get; }

    /// <summary>The field values.</summary>
    public IReadOnlyList<SmtValue> Fields { get; }

    #endregion
}
