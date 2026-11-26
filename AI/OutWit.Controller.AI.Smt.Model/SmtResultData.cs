using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// One problem's row as a node returns it, inside the blob of a result package: which problem
/// (its index, its id, the hash of the text the node was given), under which ticket, how its
/// session ended and what it came to, and what it cost.
/// <para>
/// Everything here but <see cref="WallMs"/>, <see cref="Platform"/>,
/// <see cref="ControllerVersion"/> and <see cref="Ticket"/> is a function of the problem, the
/// run's options and the solver module: two honest machines give the same values, to the last
/// byte. <see cref="Digest"/> is the SHA-256 of exactly those values
/// (<see cref="SmtResultDigest"/>), so copies of a problem solved on two machines are compared
/// by it. A row whose status is <c>node_fault</c> is the exception by its nature: it says
/// something about the machine and nothing about the problem.
/// </para>
/// <para>
/// The row names no machine: it says the platform (<c>win-x64</c>, <c>linux-x64</c>,
/// <c>osx-arm64</c>) and the controller version, and nothing else of the node. The models are
/// kept whole, however large, for the host's evaluator; what a lab is shown of them is the
/// report's to cut.
/// </para>
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtResultData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtResultData other)
            return false;

        return Index.Is(other.Index)
               && Id.Is(other.Id)
               && Hash.Is(other.Hash)
               && Ticket.Is(other.Ticket)
               && Query.Is(other.Query)
               && Status.Is(other.Status)
               && Code.Is(other.Code)
               && Message.Is(other.Message)
               && Verdict.Is(other.Verdict)
               && Reason.Is(other.Reason)
               && ReasonText.Is(other.ReasonText)
               && Proof.Is(other.Proof)
               && Count.Is(other.Count)
               && Complete.Is(other.Complete)
               && LimitReached.Is(other.LimitReached)
               && Models.Is(other.Models)
               && Values.Is(other.Values)
               && Checks.Is(other.Checks)
               && ResourceCount.Is(other.ResourceCount)
               && Fuel.Is(other.Fuel)
               && PeakMemoryBytes.Is(other.PeakMemoryBytes)
               && Transcript.Is(other.Transcript)
               && ControllerVersion.Is(other.ControllerVersion)
               && Platform.Is(other.Platform)
               && WallMs.Is(other.WallMs)
               && Digest.Is(other.Digest);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtResultData Clone()
    {
        return new SmtResultData
        {
            Index = Index,
            Id = Id,
            Hash = Hash,
            Ticket = Ticket,
            Query = Query,
            Status = Status,
            Code = Code,
            Message = Message,
            Verdict = Verdict,
            Reason = Reason,
            ReasonText = ReasonText,
            Proof = Proof,
            Count = Count,
            Complete = Complete,
            LimitReached = LimitReached,
            Models = [.. Models],
            Values = [.. Values],
            Checks = Checks,
            ResourceCount = ResourceCount,
            Fuel = Fuel,
            PeakMemoryBytes = PeakMemoryBytes,
            Transcript = Transcript,
            ControllerVersion = ControllerVersion,
            Platform = Platform,
            WallMs = WallMs,
            Digest = Digest
        };
    }

    #endregion

    #region Properties

    /// <summary>The problem's index in its set (<see cref="SmtProblemData.Index"/>).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int Index { get; set; }

    /// <summary>The problem's id (<see cref="SmtProblemData.Id"/>).</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string Id { get; set; } = "";

    /// <summary>
    /// The SHA-256 of the problem's text as the node was given it, computed by the node,
    /// lower-case hex: the name the problem came with when the text is the problem it was said
    /// to be (the row is <c>refused</c> with <c>hash_mismatch</c> when it is not).
    /// </summary>
    [MemoryPackOrder(2)]
    public string Hash { get; set; } = "";

    /// <summary>The ticket of the task package the row answers. Never written to a document.</summary>
    [MemoryPackOrder(3)]
    public string Ticket { get; set; } = "";

    /// <summary>What was asked of the problem.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public SmtQuery Query { get; set; }

    /// <summary>How the session ended, one word of <see cref="SmtResultWords.STATUSES"/>: <c>ok</c>, <c>refused</c>, <c>input_error</c>, <c>output_exceeded</c>, <c>engine_crash</c>, <c>node_fault</c>.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public string Status { get; set; } = "";

    /// <summary>The fixed word of the outcome's kind: a refusal's code, the kind of a crash or of a fault; empty where the status says it all.</summary>
    [ToString]
    [MemoryPackOrder(6)]
    public string Code { get; set; } = "";

    /// <summary>The sentence that goes with the code (a refusal's reason, the solver's message of an input error, fixed words), at most <see cref="SmtResultWords.MAX_MESSAGE_CHARACTERS"/> characters; empty for an answered query.</summary>
    [MemoryPackOrder(7)]
    public string Message { get; set; } = "";

    /// <summary>The verdict, one word of <see cref="SmtResultWords.VERDICTS"/>: <c>sat</c>, <c>unsat</c>, <c>unknown</c>, <c>none</c>.</summary>
    [ToString]
    [MemoryPackOrder(8)]
    public string Verdict { get; set; } = "";

    /// <summary>Why a check has no verdict or an enumeration is not complete, one word of <see cref="SmtResultWords.REASONS"/>: <c>rlimit</c>, <c>fuel</c>, <c>memout</c>, <c>incomplete</c>, <c>other</c>, <c>none</c>.</summary>
    [ToString]
    [MemoryPackOrder(9)]
    public string Reason { get; set; } = "";

    /// <summary>
    /// The solver's own word for an <c>unknown</c>, as it answered <c>(get-info :reason-unknown)</c>;
    /// for an enumeration that ended because the solver did not take a found model's values back
    /// (reason <c>other</c>, the models kept), what it said to that; empty when there is neither.
    /// </summary>
    [MemoryPackOrder(10)]
    public string ReasonText { get; set; } = "";

    /// <summary>What a proof came to, one word of <see cref="SmtResultWords.PROOFS"/>: <c>proved</c>, <c>counterexample</c>, <c>none</c>.</summary>
    [ToString]
    [MemoryPackOrder(11)]
    public string Proof { get; set; } = "";

    /// <summary>The models an enumeration found; 0 for the other queries.</summary>
    [ToString]
    [MemoryPackOrder(12)]
    public int Count { get; set; }

    /// <summary>Whether an enumeration is complete: its last check answered <c>unsat</c>, so <see cref="Count"/> is exact.</summary>
    [MemoryPackOrder(13)]
    public bool Complete { get; set; }

    /// <summary>Whether an enumeration stopped because it had found as many models as it was asked for.</summary>
    [MemoryPackOrder(14)]
    public bool LimitReached { get; set; }

    /// <summary>The models, each as the solver printed it in answer to <c>(get-model)</c>, byte for byte, all of them.</summary>
    [MemoryPackOrder(15)]
    public List<string> Models { get; set; } = [];

    /// <summary>The values, each as the solver printed them in answer to <c>(get-value (...))</c>: of the terms a check asked for, or of the projected constants of each model of an enumeration.</summary>
    [MemoryPackOrder(16)]
    public List<string> Values { get; set; } = [];

    /// <summary>The checks the session sent.</summary>
    [MemoryPackOrder(17)]
    public int Checks { get; set; }

    /// <summary>The solver's resource counter after the last check it answered, over the whole session.</summary>
    [MemoryPackOrder(18)]
    public long ResourceCount { get; set; }

    /// <summary>The fuel the session used, counted from the solver's first instruction.</summary>
    [MemoryPackOrder(19)]
    public long Fuel { get; set; }

    /// <summary>The solver's linear memory when the session ended (its peak), bytes.</summary>
    [MemoryPackOrder(20)]
    public long PeakMemoryBytes { get; set; }

    /// <summary>The end of the session's transcript: the commands sent, without the problem's text, and the solver's answers.</summary>
    [MemoryPackOrder(21)]
    public string Transcript { get; set; } = "";

    /// <summary>The version of the controller that solved the problem.</summary>
    [ToString]
    [MemoryPackOrder(22)]
    public string ControllerVersion { get; set; } = "";

    /// <summary>The platform it was solved on: <c>win-x64</c>, <c>linux-x64</c>, <c>osx-arm64</c>. Nothing else of the machine.</summary>
    [ToString]
    [MemoryPackOrder(23)]
    public string Platform { get; set; } = "";

    /// <summary>The wall time of the session, milliseconds: a measurement of the machine, never part of what is compared.</summary>
    [MemoryPackOrder(24)]
    public int WallMs { get; set; }

    /// <summary>The SHA-256 of the row's canonical form (<see cref="SmtResultDigest"/>), lower-case hex: the same for a problem on every honest machine.</summary>
    [MemoryPackOrder(25)]
    public string Digest { get; set; } = "";

    #endregion
}
