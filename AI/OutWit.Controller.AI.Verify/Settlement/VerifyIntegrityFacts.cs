namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>What the integrity document says of a settled run.</summary>
internal sealed class VerifyIntegrityFacts
{
    #region Properties

    /// <summary>The tasks of the run.</summary>
    public int Tasks { get; set; }

    /// <summary>The pairs of executions of one task in the first fan-out, ordered by index.</summary>
    public List<VerifyPairRecord> Pairs { get; } = [];

    /// <summary>The disputes, ordered by index.</summary>
    public List<VerifyDisputeRecord> Disputes { get; } = [];

    /// <summary>The tasks accepted on two or more executions that agree.</summary>
    public int Agreed { get; set; }

    /// <summary>The tasks accepted on one execution: not sampled, they rest on one machine.</summary>
    public int SingleExecution { get; set; }

    /// <summary>The tasks sent out once more.</summary>
    public int Repairs { get; set; }

    /// <summary>The tasks left without a result.</summary>
    public int NoResult { get; set; }

    /// <summary>The result packages refused, by reason, every round together.</summary>
    public SortedDictionary<string, int> Rejected { get; } = new(StringComparer.Ordinal);

    #endregion
}
