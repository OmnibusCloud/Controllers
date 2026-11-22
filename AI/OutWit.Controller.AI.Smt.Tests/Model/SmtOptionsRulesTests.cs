using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// The options of a run: their defaults and ceilings, which of them are out of range, and what
/// they are once the defaults are filled in.
/// </summary>
[TestFixture]
public class SmtOptionsRulesTests
{
    #region Constant Tests

    [Test]
    public void DefaultsAndCeilingsAreTheNumbersOfTheContractTest()
    {
        Assert.That((SmtOptionsRules.DEFAULT_MAX_MODELS, SmtOptionsRules.MAX_MODELS), Is.EqualTo((2, 1_000)));
        Assert.That((SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS, SmtOptionsRules.MAX_HARD_CAP_SECONDS), Is.EqualTo((60, 600)));
        Assert.That((SmtOptionsRules.DEFAULT_MEMORY_MEGABYTES, SmtOptionsRules.MAX_MEMORY_MEGABYTES), Is.EqualTo((1_024, 4_096)));
        Assert.That((SmtOptionsRules.MIN_SAMPLING_RATE, SmtOptionsRules.DEFAULT_SAMPLING_RATE, SmtOptionsRules.MAX_SAMPLING_RATE), Is.EqualTo((0d, 0.05d, 1d)));
        Assert.That(SmtOptionsRules.MAX_SEED, Is.EqualTo(2_147_483_647));
        Assert.That(SmtOptionsRules.MAX_RESOURCE_LIMIT, Is.EqualTo(4_294_967_295L));
    }

    [Test]
    public void ResourceLimitCeilingIsWhatTheScriptBuilderTakesTest()
    {
        // One number in two places: an option at the ceiling is a limit the builder writes, and
        // one above it is refused by both.
        Assert.That((ulong)SmtOptionsRules.MAX_RESOURCE_LIMIT, Is.EqualTo(SmtScripts.MAX_RESOURCE_LIMIT));
        Assert.That(SmtScripts.SetResourceLimit((ulong)SmtOptionsRules.MAX_RESOURCE_LIMIT).ToString(), Is.EqualTo("(set-option :rlimit 4294967295)"));
    }

    [Test]
    public void NewOptionsAreNotSetExceptTheSamplingRateTest()
    {
        var options = new SmtOptionsData();

        Assert.That((options.Profile, options.Query, options.MaxModels, options.Seed), Is.EqualTo(("", SmtQuery.Check, 0, 0)));
        Assert.That((options.ResourceLimit, options.HardCapSeconds, options.MemoryMegabytes), Is.EqualTo((0L, 0, 0)));
        Assert.That(options.SamplingRate, Is.EqualTo(0.05), "0 is a rate of its own: no re-execution");
        Assert.That(SmtOptionsRules.Check(options), Is.Empty);
    }

    #endregion

    #region Check Tests

    [Test]
    public void OptionsAtTheirBoundsAreInRangeTest()
    {
        var bounds = new[]
        {
            new SmtOptionsData { MaxModels = 1, Seed = 0, ResourceLimit = 1, HardCapSeconds = 1, MemoryMegabytes = 1, SamplingRate = 0 },
            new SmtOptionsData { MaxModels = 1_000, Seed = int.MaxValue, ResourceLimit = 4_294_967_295, HardCapSeconds = 600, MemoryMegabytes = 4_096, SamplingRate = 1 },
            new SmtOptionsData { Query = SmtQuery.Enumerate, Profile = "any profile: this package only carries it" },
            new SmtOptionsData { Query = SmtQuery.Prove, SamplingRate = 0.5 }
        };

        foreach (var options in bounds)
            Assert.That(SmtOptionsRules.Check(options), Is.Empty);
    }

    [Test]
    public void OptionOutOfRangeIsSaidByItsDocumentNameTest()
    {
        var cases = new (SmtOptionsData Options, string Sentence)[]
        {
            (new SmtOptionsData { Query = (SmtQuery)3 }, "'query' must be check, enumerate or prove"),
            (new SmtOptionsData { Query = (SmtQuery)(-1) }, "'query' must be check, enumerate or prove"),
            (new SmtOptionsData { MaxModels = 1_001 }, "'max_models' must be between 1 and 1000"),
            (new SmtOptionsData { MaxModels = -1 }, "'max_models' must be between 1 and 1000"),
            (new SmtOptionsData { Seed = -1 }, "'seed' must be between 0 and 2147483647"),
            (new SmtOptionsData { ResourceLimit = 4_294_967_296 }, "'rlimit' must be between 1 and 4294967295"),
            (new SmtOptionsData { ResourceLimit = -1 }, "'rlimit' must be between 1 and 4294967295"),
            (new SmtOptionsData { HardCapSeconds = 601 }, "'hard_cap_s' must be between 1 and 600"),
            (new SmtOptionsData { HardCapSeconds = -5 }, "'hard_cap_s' must be between 1 and 600"),
            (new SmtOptionsData { MemoryMegabytes = 4_097 }, "'memory_mb' must be between 1 and 4096"),
            (new SmtOptionsData { MemoryMegabytes = int.MinValue }, "'memory_mb' must be between 1 and 4096"),
            (new SmtOptionsData { SamplingRate = 1.0001 }, "'sampling_rate' must be between 0 and 1"),
            (new SmtOptionsData { SamplingRate = -0.0001 }, "'sampling_rate' must be between 0 and 1"),
            (new SmtOptionsData { SamplingRate = double.NaN }, "'sampling_rate' must be between 0 and 1"),
            (new SmtOptionsData { SamplingRate = double.PositiveInfinity }, "'sampling_rate' must be between 0 and 1")
        };

        foreach (var (options, sentence) in cases)
            Assert.That(SmtOptionsRules.Check(options), Is.EqualTo(new[] { sentence }));
    }

    [Test]
    public void EveryOptionOutOfRangeHasItsSentenceTest()
    {
        var options = new SmtOptionsData { Query = (SmtQuery)9, MaxModels = 5_000, Seed = -3, ResourceLimit = long.MaxValue, HardCapSeconds = 86_400, MemoryMegabytes = 65_536, SamplingRate = 5 };

        Assert.That(SmtOptionsRules.Check(options), Is.EqualTo(new[]
        {
            "'query' must be check, enumerate or prove",
            "'max_models' must be between 1 and 1000",
            "'seed' must be between 0 and 2147483647",
            "'rlimit' must be between 1 and 4294967295",
            "'hard_cap_s' must be between 1 and 600",
            "'memory_mb' must be between 1 and 4096",
            "'sampling_rate' must be between 0 and 1"
        }));
    }

    [Test]
    public void RulesRefuseNoOptionsAtAllTest()
    {
        Assert.Throws<ArgumentNullException>(() => SmtOptionsRules.Check(null!));
        Assert.Throws<ArgumentNullException>(() => SmtOptionsRules.Resolve(null!));
    }

    #endregion

    #region Resolve Tests

    [Test]
    public void ResolveFillsTheDefaultsThisPackageGivesTest()
    {
        var resolved = SmtOptionsRules.Resolve(new SmtOptionsData());

        Assert.That((resolved.MaxModels, resolved.HardCapSeconds, resolved.MemoryMegabytes), Is.EqualTo((2, 60, 1_024)));
        Assert.That((resolved.Profile, resolved.ResourceLimit), Is.EqualTo(("", 0L)), "the profile and its resource limit are the controller's to give");
        Assert.That((resolved.Query, resolved.Seed, resolved.SamplingRate), Is.EqualTo((SmtQuery.Check, 0, 0.05)));
        Assert.That(SmtOptionsRules.Check(resolved), Is.Empty);
    }

    [Test]
    public void ResolveKeepsWhatWasSetAndGivesACopyTest()
    {
        var options = new SmtOptionsData { Profile = "profile-a", Query = SmtQuery.Prove, MaxModels = 7, Seed = 11, ResourceLimit = 50_000_000, HardCapSeconds = 120, MemoryMegabytes = 2_048, SamplingRate = 0 };

        var resolved = SmtOptionsRules.Resolve(options);

        Assert.That(resolved, Is.Not.SameAs(options));
        Assert.That(resolved.Is(options), Is.True);
        Assert.That(resolved.SamplingRate, Is.Zero, "a rate of 0 is set: no re-execution");

        resolved.MaxModels = 9;
        Assert.That(options.MaxModels, Is.EqualTo(7), "the options as given are not touched");
    }

    [Test]
    public void ResolveChecksNothingTest()
    {
        var resolved = SmtOptionsRules.Resolve(new SmtOptionsData { MaxModels = 5_000, HardCapSeconds = -1 });

        Assert.That((resolved.MaxModels, resolved.HardCapSeconds, resolved.MemoryMegabytes), Is.EqualTo((5_000, -1, 1_024)));
    }

    #endregion
}
