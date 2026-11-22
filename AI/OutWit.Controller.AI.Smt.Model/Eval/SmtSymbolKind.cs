namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What a name a problem introduces stands for.
/// </summary>
internal enum SmtSymbolKind
{
    /// <summary>A constant or a function the problem declares and the model interprets.</summary>
    Declared = 0,

    /// <summary>A function the problem defines, or a term it names with <c>:named</c>.</summary>
    Defined = 1,

    /// <summary>A name the evaluator knows of and cannot evaluate: a term named under a binder.</summary>
    Unevaluated = 2,

    /// <summary>A constructor of a datatype.</summary>
    Constructor = 3,

    /// <summary>A selector of a constructor's field.</summary>
    Selector = 4,

    /// <summary>The tester of a constructor, in its older spelling <c>is-C</c>.</summary>
    Tester = 5
}
