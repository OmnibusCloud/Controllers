namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// The theories the generated terms are counted by: what the generator was asked for at the
/// top of a term. A term of one theory may hold terms of another underneath (a length of a
/// string in an integer term), and the core connectives everywhere.
/// </summary>
internal enum SmtDifferentialTheory
{
    /// <summary>Linear integer arithmetic: products and divisions by constants only.</summary>
    IntLinear,

    /// <summary>Non-linear integer arithmetic: products and divisions of terms.</summary>
    IntNonlinear,

    /// <summary>Linear real arithmetic, with the conversions to and from Int.</summary>
    RealLinear,

    /// <summary>Non-linear real arithmetic.</summary>
    RealNonlinear,

    /// <summary>Fixed-size bit-vectors at the widths of <see cref="SmtTermFamilyBitVectors.WIDTHS"/>.</summary>
    BitVectors,

    /// <summary>Arrays: constant arrays, stores, selects, the equality of two arrays.</summary>
    Arrays,

    /// <summary>Datatypes: a list, a tree, an enumeration; constructors, selectors, testers, match.</summary>
    Datatypes,

    /// <summary>Uninterpreted functions and a sort with the interpretations a model gives them.</summary>
    Functions,

    /// <summary>The operators of strings the evaluator computes.</summary>
    Strings,

    /// <summary>The core: ite, let, distinct, equalities and chains of comparisons over everything else.</summary>
    Core
}
