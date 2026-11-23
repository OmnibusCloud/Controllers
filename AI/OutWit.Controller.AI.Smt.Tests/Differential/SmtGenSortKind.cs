namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// What a sort of the generated terms is.
/// </summary>
internal enum SmtGenSortKind
{
    /// <summary>Bool.</summary>
    Bool,

    /// <summary>Int.</summary>
    Int,

    /// <summary>Real.</summary>
    Real,

    /// <summary>A bit-vector of a width.</summary>
    BitVector,

    /// <summary>String.</summary>
    String,

    /// <summary>An array from an index sort to an element sort.</summary>
    Array,

    /// <summary>A datatype the prelude declares.</summary>
    Datatype,

    /// <summary>A sort the prelude declares and the model populates.</summary>
    Uninterpreted
}
