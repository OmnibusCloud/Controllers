namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>The outcome of checking one model against one problem's assertions on the host.</summary>
internal sealed class SmtValidation
{
    #region Properties

    /// <summary>validated (every assertion true), failed (one false), or partial (none false, one not closed).</summary>
    public required string Class { get; init; }

    /// <summary>For partial and failed: why, in fixed words (the first open construct, or the first false assertion's index).</summary>
    public string? Reason { get; init; }

    /// <summary>The number of assertions.</summary>
    public int Assertions { get; init; }

    /// <summary>The number of assertions that evaluated to true.</summary>
    public int True { get; init; }

    /// <summary>The number of symbols the model defined (constants with a value, functions with an interpretation).</summary>
    public int Defined { get; init; }

    /// <summary>Whether a symbol the model left out was completed with its sort's default.</summary>
    public bool UsedCompletion { get; init; }

    /// <summary>Microseconds the evaluation took.</summary>
    public long Microseconds { get; init; }

    #endregion
}
