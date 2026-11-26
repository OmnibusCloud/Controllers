using OutWit.Controller.AI.Smt.Model;

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

    /// <summary>The problems first ruled on two answers or more: the first fan-out's, with the repair round's for a problem it answered.</summary>
    public int Compared { get; set; }

    /// <summary>Of those, the problems whose answers all gave one digest.</summary>
    public int Agreements { get; set; }

    /// <summary>Of those, the problems whose answers gave more than one digest.</summary>
    public int Disagreements { get; set; }

    /// <summary>The problems whose answer rests on one machine: one answer, answers not two of which are bound in one fan-out, or a lone answer of the first fan-out and one of the repair round with its digest.</summary>
    public int OneCopy { get; set; }

    /// <summary>Of those, the problems answered <c>unsat</c>: nothing but that machine vouches for them.</summary>
    public int UnsatOneCopy { get; set; }

    /// <summary>Of those, the enumerations that found models and say there is no other: their completeness is the solver's last <c>unsat</c>, which nothing but that machine vouches for.</summary>
    public int CompleteOneCopy { get; set; }

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

    /// <summary>The problems the repair round sent out once more: those without an answer from the first fan-out, and those of a package asked for twice with fewer than two answers.</summary>
    public int Repairs { get; set; }

    /// <summary>The repairs by what the first fan-out said of the problem, or <see cref="SmtRulings.COPIES_SHORT"/>.</summary>
    public SortedDictionary<string, int> RepairReasons { get; } = new(StringComparer.Ordinal);

    /// <summary>The problems with an answer of the packages asked for twice that ended without two answers of one fan-out.</summary>
    public int SampledShort { get; set; }

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

    /// <summary>The result references that came back bound (the platform told the host which package's task each was returned for), every round together.</summary>
    public int BoundResults { get; set; }

    /// <summary>The result references that came back unbound, every round together.</summary>
    public int UnboundResults { get; set; }

    /// <summary>The run's binding, one of <see cref="SmtRunWords.BINDINGS"/>.</summary>
    public string Binding => (BoundResults, UnboundResults) switch
    {
        (0, 0) => SmtRunWords.NO_RESULTS,
        (_, 0) => SmtRunWords.BOUND,
        (0, _) => SmtRunWords.UNBOUND,
        _ => SmtRunWords.MIXED
    };

    #endregion
}
