using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What a problem set and a run's options come to before anything is sent to a node
/// (<c>Smt.Preflight</c>): the set counted the way a run reads it, the options checked by name,
/// the budget of a problem as numbers, the upper bound of the work (problems times the hard cap:
/// a bound, never an estimate of how long a run takes), the memory a node needs for one session,
/// and the packages a run would be cut into. Small whatever the set: the refusals are counted by
/// code and the first <see cref="MAX_REFUSALS"/> of them are kept in full.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order: append new members at the END only.
public sealed partial class SmtPreflightData : ModelBase
{
    #region Constants

    /// <summary>The most refusals a preflight keeps in full; the rest are counted by code.</summary>
    public const int MAX_REFUSALS = 100;

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtPreflightData other)
            return false;

        return Runnable.Is(other.Runnable)
               && Rows.Is(other.Rows)
               && Accepted.Is(other.Accepted)
               && Refused.Is(other.Refused)
               && BytesRead.Is(other.BytesRead)
               && Sha256.Is(other.Sha256)
               && RefusalCodes.Is(other.RefusalCodes)
               && Refusals.Is(other.Refusals)
               && Logics.Is(other.Logics)
               && OtherLogics.Is(other.OtherLogics)
               && Queries.Is(other.Queries)
               && Expected.Is(other.Expected)
               && Stripped.Is(other.Stripped)
               && Licenses.Is(other.Licenses)
               && OtherLicenses.Is(other.OtherLicenses)
               && OptionProblems.Is(other.OptionProblems)
               && Profile.Is(other.Profile)
               && ResourceLimit.Is(other.ResourceLimit)
               && HardCapSeconds.Is(other.HardCapSeconds)
               && Fuel.Is(other.Fuel)
               && MemoryMegabytes.Is(other.MemoryMegabytes)
               && MaxModels.Is(other.MaxModels)
               && Seed.Is(other.Seed)
               && SamplingRate.Is(other.SamplingRate, tolerance)
               && SessionMemoryBytes.Is(other.SessionMemoryBytes)
               && UpperBoundSeconds.Is(other.UpperBoundSeconds)
               && Packages.Is(other.Packages)
               && ProblemsPerPackage.Is(other.ProblemsPerPackage)
               && Messages.Is(other.Messages);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtPreflightData Clone()
    {
        return new SmtPreflightData
        {
            Runnable = Runnable,
            Rows = Rows,
            Accepted = Accepted,
            Refused = Refused,
            BytesRead = BytesRead,
            Sha256 = Sha256,
            RefusalCodes = [.. RefusalCodes.Select(count => count.Clone())],
            Refusals = [.. Refusals.Select(refusal => refusal.Clone())],
            Logics = [.. Logics.Select(count => count.Clone())],
            OtherLogics = OtherLogics,
            Queries = [.. Queries.Select(count => count.Clone())],
            Expected = [.. Expected.Select(count => count.Clone())],
            Stripped = [.. Stripped.Select(count => count.Clone())],
            Licenses = [.. Licenses.Select(count => count.Clone())],
            OtherLicenses = OtherLicenses,
            OptionProblems = [.. OptionProblems],
            Profile = Profile,
            ResourceLimit = ResourceLimit,
            HardCapSeconds = HardCapSeconds,
            Fuel = Fuel,
            MemoryMegabytes = MemoryMegabytes,
            MaxModels = MaxModels,
            Seed = Seed,
            SamplingRate = SamplingRate,
            SessionMemoryBytes = SessionMemoryBytes,
            UpperBoundSeconds = UpperBoundSeconds,
            Packages = Packages,
            ProblemsPerPackage = ProblemsPerPackage,
            Messages = [.. Messages]
        };
    }

    #endregion

    #region Properties

    /// <summary>Whether a run would start: the options are in range, the profile is the controller's, and the set has a row. A set whose every row is refused still runs: its results say why for each.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public bool Runnable { get; set; }

    /// <summary>The rows of the set, taken and refused (blank lines are no rows).</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int Rows { get; set; }

    /// <summary>The rows taken: the problems a run sends to nodes.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int Accepted { get; set; }

    /// <summary>The rows refused.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int Refused { get; set; }

    /// <summary>The bytes of the set read.</summary>
    [MemoryPackOrder(4)]
    public long BytesRead { get; set; }

    /// <summary>The SHA-256 of the set's bytes, lower-case hex: the name the provenance of a run gives it.</summary>
    [MemoryPackOrder(5)]
    public string Sha256 { get; set; } = "";

    /// <summary>The refused rows by code, in the order the codes were first met.</summary>
    [MemoryPackOrder(6)]
    public List<SmtCountData> RefusalCodes { get; set; } = [];

    /// <summary>The first refusals in full, at most <see cref="MAX_REFUSALS"/>: the row, the id, the line in the problem, the command, the code and the reason (never the text).</summary>
    [MemoryPackOrder(7)]
    public List<SmtRefusalData> Refusals { get; set; } = [];

    /// <summary>The taken problems by logic (the name a problem's <c>set-logic</c> gives, empty for none).</summary>
    [MemoryPackOrder(8)]
    public List<SmtCountData> Logics { get; set; } = [];

    /// <summary>The taken problems of logics past the 64 a summary names one by one.</summary>
    [MemoryPackOrder(9)]
    public int OtherLogics { get; set; }

    /// <summary>The taken problems by query: <c>check</c>, <c>enumerate</c>, <c>prove</c>.</summary>
    [MemoryPackOrder(10)]
    public List<SmtCountData> Queries { get; set; } = [];

    /// <summary>The taken problems by expected verdict: <c>sat</c>, <c>unsat</c>, <c>none</c>.</summary>
    [MemoryPackOrder(11)]
    public List<SmtCountData> Expected { get; set; } = [];

    /// <summary>The commands the sanitizer left out of the taken problems (the query decides what runs), by name.</summary>
    [MemoryPackOrder(12)]
    public List<SmtCountData> Stripped { get; set; } = [];

    /// <summary>The taken problems by the licence their <c>:license</c> names (empty for none).</summary>
    [MemoryPackOrder(13)]
    public List<SmtCountData> Licenses { get; set; } = [];

    /// <summary>The taken problems of licences past the 64 a summary names one by one.</summary>
    [MemoryPackOrder(14)]
    public int OtherLicenses { get; set; }

    /// <summary>What is wrong with the options, one sentence an option, each named as a run's specification names it; empty when nothing is.</summary>
    [MemoryPackOrder(15)]
    public List<string> OptionProblems { get; set; } = [];

    /// <summary>The solver profile a run would use.</summary>
    [ToString]
    [MemoryPackOrder(16)]
    public string Profile { get; set; } = "";

    /// <summary>The resource limit of one check, as a number (the profile's when the options name none).</summary>
    [MemoryPackOrder(17)]
    public long ResourceLimit { get; set; }

    /// <summary>The hard cap of one problem in seconds of the reference machine.</summary>
    [MemoryPackOrder(18)]
    public int HardCapSeconds { get; set; }

    /// <summary>The fuel of one problem: the hard cap as the count the sandbox stops at.</summary>
    [MemoryPackOrder(19)]
    public long Fuel { get; set; }

    /// <summary>The most memory of one problem, megabytes.</summary>
    [MemoryPackOrder(20)]
    public int MemoryMegabytes { get; set; }

    /// <summary>The models an enumeration whose row names no number looks for.</summary>
    [MemoryPackOrder(21)]
    public int MaxModels { get; set; }

    /// <summary>The seed of the solver's random choices.</summary>
    [MemoryPackOrder(22)]
    public int Seed { get; set; }

    /// <summary>The share of the packages a run asks to solve on two machines, 0 to 1.</summary>
    [MemoryPackOrder(23)]
    public double SamplingRate { get; set; }

    /// <summary>The memory a node needs for one session under the options, bytes: a node with less available refuses the work.</summary>
    [MemoryPackOrder(24)]
    public long SessionMemoryBytes { get; set; }

    /// <summary>The upper bound of the work: the taken problems times the hard cap, seconds of the reference machine. A bound, not an estimate.</summary>
    [MemoryPackOrder(25)]
    public long UpperBoundSeconds { get; set; }

    /// <summary>The packages a run would cut the taken problems into (before any copy).</summary>
    [MemoryPackOrder(26)]
    public int Packages { get; set; }

    /// <summary>The most problems a package of the run holds.</summary>
    [MemoryPackOrder(27)]
    public int ProblemsPerPackage { get; set; }

    /// <summary>What else a lab should know before it runs the set, one sentence each.</summary>
    [MemoryPackOrder(28)]
    public List<string> Messages { get; set; } = [];

    #endregion
}
