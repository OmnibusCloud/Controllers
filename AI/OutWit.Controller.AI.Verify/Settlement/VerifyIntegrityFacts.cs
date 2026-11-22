namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>What the integrity document says of a settled run.</summary>
internal sealed class VerifyIntegrityFacts
{
    #region Properties

    /// <summary>The tasks of the run.</summary>
    public int Tasks { get; set; }

    /// <summary>
    /// The pairs of executions of one task in the fan-out it was first ruled on (the first one,
    /// or the repair round for a task the first left without a verdict), ordered by index.
    /// </summary>
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

    /// <summary>The sampling rate the script stated, 0 to 1; null when it stated none.</summary>
    public double? SamplingRate { get; set; }

    /// <summary>Whether the sampling that was asked for happened (<see cref="VerifySampling"/>).</summary>
    public string Sampling { get; set; } = VerifySampling.NOT_STATED;

    /// <summary>The fan-outs of the run in numbers, in the order they ran.</summary>
    public List<VerifyFanOutRecord> FanOuts { get; } = [];

    /// <summary>The task packages no accepted answer came back for, every fan-out together.</summary>
    public int UnansweredPackages => FanOuts.Sum(fanOut => fanOut.Unanswered);

    #endregion
}
