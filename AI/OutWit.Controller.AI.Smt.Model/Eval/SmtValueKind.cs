namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What a value of the evaluator is. The order is the order of values of different kinds among
/// the indices of an array.
/// </summary>
internal enum SmtValueKind
{
    /// <summary>A truth value.</summary>
    Bool = 0,

    /// <summary>An integer.</summary>
    Int = 1,

    /// <summary>A rational of sort Real.</summary>
    Real = 2,

    /// <summary>A bit-vector.</summary>
    BitVector = 3,

    /// <summary>A string.</summary>
    String = 4,

    /// <summary>A constructor of a datatype applied to values.</summary>
    Datatype = 5,

    /// <summary>An element of a sort the problem leaves to the model.</summary>
    Element = 6,

    /// <summary>An array.</summary>
    Array = 7
}
