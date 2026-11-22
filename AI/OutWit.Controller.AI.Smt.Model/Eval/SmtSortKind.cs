namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What a sort is to the evaluator.
/// </summary>
internal enum SmtSortKind
{
    /// <summary>Bool.</summary>
    Bool = 0,

    /// <summary>Int.</summary>
    Int = 1,

    /// <summary>Real.</summary>
    Real = 2,

    /// <summary>String.</summary>
    String = 3,

    /// <summary>A bit-vector of a fixed width.</summary>
    BitVector = 4,

    /// <summary>An array from one index sort to an element sort.</summary>
    Array = 5,

    /// <summary>An algebraic datatype the problem declares, with its sort arguments.</summary>
    Datatype = 6,

    /// <summary>A sort the problem declares and leaves to the model, with its sort arguments.</summary>
    Uninterpreted = 7,

    /// <summary>A sort the evaluator has no values for.</summary>
    Unsupported = 8
}
