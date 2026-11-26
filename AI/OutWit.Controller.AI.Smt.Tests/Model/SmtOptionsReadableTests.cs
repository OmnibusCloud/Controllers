using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// The options a reader of a problem set takes (a preflight reads a set whatever its options
/// say): the options themselves when they are in range, else each option out of range at the
/// value that means its default. The ranges are written once, for the check and for this, so
/// that whatever a check refuses this puts back in range.
/// </summary>
[TestFixture]
public sealed class SmtOptionsReadableTests
{
    #region Readable Tests

    [Test]
    public void EveryOptionOutOfRangeIsPutBackInRangeTest()
    {
        var outOfRange = new (string Name, Action<SmtOptionsData> Change)[]
        {
            ("query", options => options.Query = (SmtQuery)9),
            ("max_models", options => options.MaxModels = -1),
            ("max_models", options => options.MaxModels = SmtOptionsRules.MAX_MODELS + 1),
            ("seed", options => options.Seed = -1),
            ("rlimit", options => options.ResourceLimit = -1),
            ("rlimit", options => options.ResourceLimit = SmtOptionsRules.MAX_RESOURCE_LIMIT + 1),
            ("hard_cap_s", options => options.HardCapSeconds = -1),
            ("hard_cap_s", options => options.HardCapSeconds = SmtOptionsRules.MAX_HARD_CAP_SECONDS + 1),
            ("memory_mb", options => options.MemoryMegabytes = -1),
            ("memory_mb", options => options.MemoryMegabytes = SmtOptionsRules.MAX_MEMORY_MEGABYTES + 1),
            ("sampling_rate", options => options.SamplingRate = -0.5),
            ("sampling_rate", options => options.SamplingRate = 1.5),
            ("sampling_rate", options => options.SamplingRate = double.NaN)
        };

        Assert.Multiple(() =>
        {
            foreach (var (name, change) in outOfRange)
            {
                var options = new SmtOptionsData();
                change(options);

                Assert.That(SmtOptionsRules.Check(options), Has.One.StartsWith($"'{name}'"), $"{name} is out of range");
                Assert.That(SmtOptionsRules.Check(SmtOptionsRules.Readable(options)), Is.Empty, $"{name} is put back in range");
            }

            var all = new SmtOptionsData();
            foreach (var (_, change) in outOfRange)
                change(all);

            Assert.That(SmtOptionsRules.Check(SmtOptionsRules.Readable(all)), Is.Empty, "every option at once");
        });
    }

    [Test]
    public void AnOptionOutOfRangeIsPutBackAtTheValueThatMeansItsDefaultTest()
    {
        var options = new SmtOptionsData
        {
            Query = (SmtQuery)9, MaxModels = -1, Seed = -1, ResourceLimit = -1, HardCapSeconds = 601, MemoryMegabytes = 4_097, SamplingRate = 2, Profile = "z3-5.4.1"
        };

        var readable = SmtOptionsRules.Readable(options);

        Assert.Multiple(() =>
        {
            Assert.That(readable, Is.Not.SameAs(options), "a copy");
            Assert.That(options.Seed, Is.EqualTo(-1), "the options as given are left as they are");
            Assert.That((readable.Query, readable.MaxModels, readable.Seed, readable.ResourceLimit, readable.HardCapSeconds, readable.MemoryMegabytes),
                Is.EqualTo((SmtQuery.Check, 0, 0, 0L, 0, 0)));
            Assert.That(readable.SamplingRate, Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE));
            Assert.That(readable.Profile, Is.EqualTo("z3-5.4.1"), "what is not a range is left as it is");
        });
    }

    [Test]
    public void OptionsInRangeAreTakenAsTheyAreTest()
    {
        var options = new SmtOptionsData { Query = SmtQuery.Enumerate, MaxModels = 7, Seed = 3, HardCapSeconds = 600, MemoryMegabytes = 4_096, SamplingRate = 0 };

        Assert.That(SmtOptionsRules.Readable(options), Is.SameAs(options));
    }

    #endregion
}
