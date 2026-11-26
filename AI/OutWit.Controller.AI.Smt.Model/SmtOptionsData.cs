using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// The options of a run. A number that is 0, and a text that is empty, means "not set: the
/// default" (<see cref="SmtOptionsRules"/> holds the defaults and the ceilings). The one
/// exception is <see cref="SamplingRate"/>, where 0 is a value of its own: a new instance holds
/// the default there.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtOptionsData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtOptionsData other)
            return false;

        return Profile.Is(other.Profile)
               && Query.Is(other.Query)
               && MaxModels.Is(other.MaxModels)
               && Seed.Is(other.Seed)
               && ResourceLimit.Is(other.ResourceLimit)
               && HardCapSeconds.Is(other.HardCapSeconds)
               && MemoryMegabytes.Is(other.MemoryMegabytes)
               && SamplingRate.Is(other.SamplingRate, tolerance);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtOptionsData Clone()
    {
        return new SmtOptionsData
        {
            Profile = Profile,
            Query = Query,
            MaxModels = MaxModels,
            Seed = Seed,
            ResourceLimit = ResourceLimit,
            HardCapSeconds = HardCapSeconds,
            MemoryMegabytes = MemoryMegabytes,
            SamplingRate = SamplingRate
        };
    }

    #endregion

    #region Properties

    /// <summary><c>profile</c>: the id of the solver profile (the solver's build and its settings); empty = the controller's default.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string Profile { get; set; } = "";

    /// <summary><c>query</c>: the query of a row that names none.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public SmtQuery Query { get; set; }

    /// <summary><c>max_models</c>: the most models an enumeration looks for when its row names no number; 0 = the default.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int MaxModels { get; set; }

    /// <summary><c>seed</c>: the seed of the solver's random choices, 0 or more.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int Seed { get; set; }

    /// <summary><c>rlimit</c>: the solver's own counter of work for one check; 0 = the profile's default.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public long ResourceLimit { get; set; }

    /// <summary>
    /// <c>hard_cap_s</c>: the most seconds of a reference machine one problem may take; 0 = the
    /// default. The controller turns the seconds into a budget of the solver's work that is the
    /// same on every node; this type only carries the number.
    /// </summary>
    [ToString]
    [MemoryPackOrder(5)]
    public int HardCapSeconds { get; set; }

    /// <summary><c>memory_mb</c>: the most memory one problem may take, in megabytes; 0 = the default.</summary>
    [ToString]
    [MemoryPackOrder(6)]
    public int MemoryMegabytes { get; set; }

    /// <summary>
    /// <c>sampling_rate</c>: the share of the packages that are solved on two machines, 0 to 1. 0
    /// turns re-execution off and 1 solves every package twice, so "not set" has no number of its
    /// own here: a new instance holds the default. The share is rounded up to a whole package, so
    /// any rate above 0 solves one package twice at least: a small set has more of its problems
    /// twice than the rate says, and a set of one package (up to a package's size) is solved twice
    /// whole, wherever the pool has two machines.
    /// </summary>
    [ToString]
    [MemoryPackOrder(7)]
    public double SamplingRate { get; set; } = SmtOptionsRules.DEFAULT_SAMPLING_RATE;

    #endregion
}
