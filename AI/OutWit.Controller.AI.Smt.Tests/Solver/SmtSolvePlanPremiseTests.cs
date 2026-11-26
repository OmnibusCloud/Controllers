using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The premise the host's rule for a node's <c>refused</c> rests on: a node refuses a problem only
/// for what the host has already checked with the same code before a package is made. Over a
/// corpus of rows (the solver's test problems, the sanitizer's adversarial corpus, the model
/// evaluator's fixtures, fuzzed texts, and the fields of every query at and past their bounds):
/// a row the host takes is a problem an honest node's plan takes, under every options the host
/// starts a run on; and whatever a node's plan refuses when it is handed a row nobody read, the
/// host's reader refuses too. The codes are the model package's own list, so a code that is added
/// later is held the same way.
/// </summary>
[TestFixture]
public sealed class SmtSolvePlanPremiseTests
{
    #region Constants

    private const int SEED = 20261126;
    private const int FUZZED = 1_000;
    private const string ADVERSARIAL = "Fixtures/Adversarial";
    private const string LIA = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (> x y))(check-sat)";
    private const string ALPHABET = "()|\"\\;: #_!xyzw0123456789.-+=<>abdeftsl\n\r\t";

    #endregion

    #region Premise Tests

    [Test]
    public void EveryRowTheHostTakesIsOneAnHonestNodeTakesTest()
    {
        var taken = 0;
        var refusedByTheNode = new List<string>();

        foreach (var options in RunOptions())
        {
            foreach (var row in Corpus())
            {
                var (rows, _) = SmtProblemSets.Read(row.Line(), options);
                var read = rows.Single();
                if (!read.IsAccepted)
                    continue;

                taken++;
                if (SmtPremiseRig.NodeRefusal(read.Data, SmtPremiseRig.Resolve(options)) is { } code)
                    refusedByTheNode.Add($"{row.Id} ({row.Query}): {code}");
            }
        }

        TestContext.Out.WriteLine($"{taken} rows taken by the host");
        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.GreaterThan(1_000), "the corpus reaches the host's every query");
            Assert.That(refusedByTheNode, Is.Empty, "an honest node's plan refuses no problem the host took");
        });
    }

    [Test]
    public void EveryRunTheHostStartsHasOptionsAnHonestNodeTakesTest()
    {
        var started = 0;
        var refusedByTheNode = new List<string>();

        foreach (var options in EveryOptions())
        {
            if (!SmtPremiseRig.HostStarts(options))
                continue;

            started++;
            var problem = SmtProblemSets.Accept(new SmtRawRow("o", SmtQuery.Check, LIA).Line(), options);
            if (SmtPremiseRig.NodeRefusal(problem, SmtPremiseRig.Resolve(options)) is { } code)
                refusedByTheNode.Add($"{options}: {code}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.GreaterThan(20));
            Assert.That(refusedByTheNode, Is.Empty, "the options of a run the host started are options a node takes");
        });
    }

    [Test]
    public void WhatANodeRefusesTheHostRefusesBeforeAPackageIsMadeTest()
    {
        // A row handed to a node's plan as it was written, with the hash its text has when the
        // host's sanitizer takes it: whatever the plan refuses, the host's reader refuses the
        // same row, and with a code the node may give.
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unrefused = new List<string>();
        var otherCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var options = new SmtOptionsData();
        var resolved = SmtPremiseRig.Resolve(options);

        foreach (var row in Corpus())
        {
            var sanitized = SmtSanitizer.Sanitize(row.Smt2);
            var hash = sanitized.IsAccepted ? sanitized.Problem.Hash : new string('0', 64);
            if (SmtPremiseRig.NodeRefusal(row.Problem(hash), resolved) is not { } code)
                continue;

            codes[code] = codes.GetValueOrDefault(code) + 1;
            var (rows, _) = SmtProblemSets.Read(row.Line(), options);
            var read = rows.Single();
            if (read.IsAccepted)
                unrefused.Add($"{row.Id} ({row.Query}): the node refuses it with {code}");
            else if (read.Refusal!.Code != code)
                otherCodes[$"{code} -> {read.Refusal.Code}"] = otherCodes.GetValueOrDefault($"{code} -> {read.Refusal.Code}") + 1;
        }

        foreach (var (code, count) in codes)
            TestContext.Out.WriteLine($"  the node refuses with {code}: {count}");
        foreach (var (pair, count) in otherCodes)
            TestContext.Out.WriteLine($"  the host refuses it with another code ({pair}): {count}");

        Assert.Multiple(() =>
        {
            Assert.That(unrefused, Is.Empty, "the host refuses whatever a node's plan refuses");
            Assert.That(codes.Keys, Is.SubsetOf(SmtPremiseRig.NodeCodes), "and a node refuses with the codes of the list only");
            Assert.That(codes.Keys.Intersect(SmtRefusalCodes.All).Count(), Is.GreaterThan(8), "the corpus reaches the node's checks");
        });
    }

    [Test]
    public void AHashTheTextDoesNotHaveIsTheOnlyRefusalTheHostCannotMeetTest()
    {
        // The host writes the hash of the text it took; a node refuses a text whose hash is another.
        // No row can carry a hash, so the host meets that refusal only in a package it did not make.
        var row = new SmtRawRow("h", SmtQuery.Check, LIA);
        var problem = SmtProblemSets.Accept(row.Line());
        var resolved = SmtPremiseRig.Resolve(new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(SmtPremiseRig.NodeRefusal(problem, resolved), Is.Null, "the host's own hash");
            Assert.That(SmtPremiseRig.NodeRefusal(row.Problem(new string('0', 64)), resolved), Is.EqualTo(SmtPremiseRig.NodeOwnCodes.First()), "another hash");
        });
    }

    #endregion

    #region Tools

    /// <summary>The options of the runs the corpus is read under: the defaults, and the ceilings of the fields a row may leave to the run.</summary>
    private static IEnumerable<SmtOptionsData> RunOptions()
    {
        yield return new SmtOptionsData();
        yield return new SmtOptionsData { Query = SmtQuery.Enumerate, MaxModels = SmtOptionsRules.MAX_MODELS };
        yield return new SmtOptionsData { Query = SmtQuery.Prove, MaxModels = 1 };
    }

    /// <summary>Options of every field at its floor, its default, its ceiling and past them, one field at a time.</summary>
    private static IEnumerable<SmtOptionsData> EveryOptions()
    {
        yield return new SmtOptionsData();
        foreach (var models in new[] { -1, 0, 1, SmtOptionsRules.MAX_MODELS, SmtOptionsRules.MAX_MODELS + 1 })
            yield return new SmtOptionsData { MaxModels = models };

        foreach (var seed in new[] { -1, 0, 7, int.MaxValue })
            yield return new SmtOptionsData { Seed = seed };

        foreach (var rlimit in new[] { -1L, 0L, 1L, SmtOptionsRules.MAX_RESOURCE_LIMIT, SmtOptionsRules.MAX_RESOURCE_LIMIT + 1 })
            yield return new SmtOptionsData { ResourceLimit = rlimit };

        foreach (var cap in new[] { -1, 0, 1, SmtOptionsRules.MAX_HARD_CAP_SECONDS, SmtOptionsRules.MAX_HARD_CAP_SECONDS + 1 })
            yield return new SmtOptionsData { HardCapSeconds = cap };

        foreach (var memory in new[] { -1, 0, 1, SmtOptionsRules.MAX_MEMORY_MEGABYTES, SmtOptionsRules.MAX_MEMORY_MEGABYTES + 1 })
            yield return new SmtOptionsData { MemoryMegabytes = memory };

        foreach (var rate in new[] { -0.5, 0, 0.05, 1, 1.5, double.NaN })
            yield return new SmtOptionsData { SamplingRate = rate };

        foreach (var profile in new[] { "", "z3-5.4.1", "z3-4.13.0", "Z3-5.4.1" })
            yield return new SmtOptionsData { Profile = profile };

        foreach (var query in new[] { SmtQuery.Check, SmtQuery.Enumerate, SmtQuery.Prove, (SmtQuery)7 })
            yield return new SmtOptionsData { Query = query };
    }

    /// <summary>The corpus: texts of every kind, each with the fields of every query, at and past their bounds.</summary>
    private static IEnumerable<SmtRawRow> Corpus()
    {
        var number = 0;
        foreach (var text in Texts())
        {
            foreach (var row in Fields(text))
                yield return row with { Id = $"r{number++}" };
        }
    }

    /// <summary>The texts: the solver's test problems, the evaluator's fixtures, the adversarial corpus, and fuzzed problems.</summary>
    private static IEnumerable<string> Texts()
    {
        yield return LIA;

        foreach (var solverCase in SmtSolverCases.All)
            yield return solverCase.Text;

        foreach (var fixture in SmtFixtures.All)
            yield return fixture.Problem;

        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, ADVERSARIAL.Replace('/', Path.DirectorySeparatorChar));
        foreach (var file in Directory.EnumerateFiles(folder, "*.smt2").Order(StringComparer.Ordinal))
            yield return Encoding.UTF8.GetString(File.ReadAllBytes(file));

        var random = new Random(SEED);
        for (var fuzzed = 0; fuzzed < FUZZED; fuzzed++)
            yield return Fuzz(random, fuzzed % 2 == 0 ? LIA : SmtFixtures.All[random.Next(SmtFixtures.All.Count)].Problem);
    }

    /// <summary>A text changed in one to four places: a character put in, taken out or put in place of another.</summary>
    private static string Fuzz(Random random, string text)
    {
        var fuzzed = new StringBuilder(text);
        var changes = 1 + random.Next(4);
        for (var change = 0; change < changes && fuzzed.Length > 0; change++)
        {
            var at = random.Next(fuzzed.Length);
            var character = ALPHABET[random.Next(ALPHABET.Length)];
            switch (random.Next(3))
            {
                case 0:
                    fuzzed.Insert(at, character);
                    break;
                case 1:
                    fuzzed.Remove(at, 1);
                    break;
                default:
                    fuzzed[at] = character;
                    break;
            }
        }

        return fuzzed.ToString();
    }

    /// <summary>The rows of a text: each query, bare and with its fields, good ones, bad ones and ones past their bounds.</summary>
    private static IEnumerable<SmtRawRow> Fields(string text)
    {
        yield return new SmtRawRow("", SmtQuery.Check, text);
        yield return new SmtRawRow("", SmtQuery.Check, text, Values: ["x"]);
        yield return new SmtRawRow("", SmtQuery.Check, text, Values: ["(+ x y)", "w", "(", "|x|", "(_ bv1 8)", "#b1"]);
        yield return new SmtRawRow("", SmtQuery.Check, text, Values: Enumerable.Repeat("x", SmtProblemSetLimits.MAX_VALUES).ToList());
        yield return new SmtRawRow("", SmtQuery.Check, text, Values: Enumerable.Repeat("x", SmtProblemSetLimits.MAX_VALUES + 1).ToList());
        yield return new SmtRawRow("", SmtQuery.Check, text, Values: []);

        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["x"]);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["x", "y"], MaxModels: SmtOptionsRules.MAX_MODELS);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["|x|"], MaxModels: 1);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["x", "x"]);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["(+ x 1)"]);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["w"]);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["("]);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: []);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: Enumerable.Range(0, SmtProblemSetLimits.MAX_PROJECT + 1).Select(index => $"c{index}").ToList());
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["x"], MaxModels: 0);
        yield return new SmtRawRow("", SmtQuery.Enumerate, text, Project: ["x"], MaxModels: SmtOptionsRules.MAX_MODELS + 1);

        yield return new SmtRawRow("", SmtQuery.Prove, text, Goal: "(> x 0)");
        yield return new SmtRawRow("", SmtQuery.Prove, text, Goal: "x");
        yield return new SmtRawRow("", SmtQuery.Prove, text, Goal: "(");
        yield return new SmtRawRow("", SmtQuery.Prove, text, Goal: "");
        yield return new SmtRawRow("", SmtQuery.Prove, text);
    }

    #endregion
}
