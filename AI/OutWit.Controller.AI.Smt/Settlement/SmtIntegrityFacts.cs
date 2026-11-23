namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What the integrity document says of a settled run: counts, and records by problem index
/// only. Nothing here names a package, a ticket or a machine.
/// </summary>
internal sealed class SmtIntegrityFacts
{
    #region Functions

    /// <summary>Counts one under a key.</summary>
    /// <param name="counts">The counts.</param>
    /// <param name="key">The key.</param>
    public static void Count(SortedDictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    #endregion

    #region Properties

    /// <summary>The taken problems of the run.</summary>
    public int Problems { get; set; }

    /// <summary>The answers taken, every round together: a copy of a problem's row that is a statement about it.</summary>
    public long CopiesMade { get; set; }

    /// <summary>The problems whose first round brought two answers or more.</summary>
    public int Compared { get; set; }

    /// <summary>Of those, the problems whose answers all gave one digest.</summary>
    public int Agreements { get; set; }

    /// <summary>Of those, the problems whose answers gave more than one digest.</summary>
    public int Disagreements { get; set; }

    /// <summary>The problems settled on one answer: they rest on one machine.</summary>
    public int OneCopy { get; set; }

    /// <summary>Of those, the problems answered <c>unsat</c>: nothing but that machine vouches for them.</summary>
    public int UnsatOneCopy { get; set; }

    /// <summary>The disputes, ordered by index.</summary>
    public List<SmtDisputeRecord> Disputes { get; } = [];

    /// <summary>The answers that differ from the settled answer of the others: a lie was found in each.</summary>
    public int Lies { get; set; }

    /// <summary>The problems a lie was found in.</summary>
    public int ProblemsWithLies { get; set; }

    /// <summary>The answers whose model the evaluator called failed, every round together.</summary>
    public int FailedModels { get; set; }

    /// <summary>The problems the machines agree on and the evaluator calls failed: disagreements between the evaluator and the solver.</summary>
    public List<(int Index, string Reason, string Detail)> EvaluatorDisagreements { get; } = [];

    /// <summary>The problems without an answer from the first fan-out, sent out once more.</summary>
    public int Repairs { get; set; }

    /// <summary>The repairs by what the first fan-out said of the problem.</summary>
    public SortedDictionary<string, int> RepairReasons { get; } = new(StringComparer.Ordinal);

    /// <summary>The problems left without a result.</summary>
    public int NoResult { get; set; }

    /// <summary>The problems left without a result, by what the nodes said last.</summary>
    public SortedDictionary<string, int> NoResultReasons { get; } = new(StringComparer.Ordinal);

    /// <summary>The result packages refused, by reason, every round together.</summary>
    public SortedDictionary<string, int> Rejected { get; } = new(StringComparer.Ordinal);

    /// <summary>The packages a node said it could not work on, by the node's code, every round together.</summary>
    public SortedDictionary<string, int> Faults { get; } = new(StringComparer.Ordinal);

    /// <summary>The sampling rate the script stated, 0 to 1; null when it stated none.</summary>
    public double? SamplingRate { get; set; }

    /// <summary>Whether the sampling that was asked for happened (<see cref="SmtSampling"/>).</summary>
    public string Sampling { get; set; } = SmtSampling.NOT_STATED;

    /// <summary>The fan-outs of the run in numbers, in the order they ran.</summary>
    public List<SmtFanOutRecord> FanOuts { get; } = [];

    /// <summary>The packages of problems no taken answer came back for, every fan-out together.</summary>
    public int UnansweredPackages => FanOuts.Sum(fanOut => fanOut.Unanswered);

    #endregion
}
