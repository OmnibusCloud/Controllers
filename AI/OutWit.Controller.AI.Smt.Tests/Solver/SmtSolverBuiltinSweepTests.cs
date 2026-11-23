using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// How the names of <c>Fixtures/Solver/builtin-names.tsv</c> were found, as a test to run again
/// when the solver module is pinned anew: every string of the module that could be a symbol (each
/// token of its strings, and each token's every tail, since the linker keeps a string inside the
/// tail of a longer one) is given to the solver bare, <c>(= N N)</c>, with no logic and under
/// <c>ALL</c>, and applied, <c>(N true)</c>, <c>(N 0 0)</c>, <c>(N true 0 0)</c>, under
/// <c>ALL</c>. A name the solver calls an unknown constant every time is no name of its own;
/// every other one is. The set found must be the fixture's.
/// <para>
/// About ninety thousand candidates, four hundred thousand questions: a quarter of an hour on a
/// workstation. Explicit: it runs only when asked for by name.
/// </para>
/// </summary>
[TestFixture]
[Explicit("a quarter of an hour: run it when the solver module is pinned anew")]
[Category("Sweep")]
public sealed class SmtSolverBuiltinSweepTests
{
    #region Constants

    private const string FIXTURE = "Fixtures/Solver/builtin-names.tsv";
    private const int LONGEST_STRING = 400;
    private const int LONGEST_TOKEN = 48;
    private const int PARALLEL = 8;

    private static readonly Regex TOKEN = new("[A-Za-z0-9~!@$%^&*_\\-+=<>.?/]+", RegexOptions.CultureInvariant);

    private static readonly string[] RESERVED = ["let", "forall", "exists", "match", "par", "_", "!", "as", "NUMERAL", "DECIMAL", "STRING", "BINARY", "HEXADECIMAL"];

    private static readonly (string Prefix, string Form)[] QUESTIONS =
    [
        ("", "N"), ("(set-logic ALL)", "N"), ("(set-logic ALL)", "(N true)"), ("(set-logic ALL)", "(N 0 0)"), ("(set-logic ALL)", "(N true 0 0)")
    ];

    #endregion

    #region Sweep Tests

    [Test]
    public void EveryNameTheSolverHasBuiltInIsInTheFixtureTest()
    {
        var module = File.ReadAllBytes(SmtSolverModule.Require());
        SmtSolverRig.Warm();

        var candidates = Candidates(module);
        var found = new ConcurrentBag<string>();
        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = PARALLEL }, name =>
        {
            foreach (var (prefix, form) in QUESTIONS)
            {
                var term = form.Replace("N", name, StringComparison.Ordinal);
                var answer = SmtRawSolver.Ask($"{prefix}(assert (= {term} {term}))");
                if (!answer.Contains($"unknown constant {name}", StringComparison.Ordinal))
                {
                    found.Add(name);
                    return;
                }
            }
        });

        var recorded = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, FIXTURE.Replace('/', Path.DirectorySeparatorChar)))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t')[0])
            .ToList();

        TestContext.Out.WriteLine($"{candidates.Count} candidates, {found.Count} names the solver has built in");
        Assert.That(found.Distinct().Order(StringComparer.Ordinal), Is.EqualTo(recorded.Order(StringComparer.Ordinal)));
    }

    #endregion

    #region Tools

    /// <summary>The strings of the module that could be simple symbols: no digit first, no reserved word, no number written with a minus.</summary>
    private static List<string> Candidates(byte[] module)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;
        for (var index = 0; index <= module.Length; index++)
        {
            if (index < module.Length && module[index] != 0)
                continue;

            if (index - start <= LONGEST_STRING)
            {
                var text = System.Text.Encoding.Latin1.GetString(module, start, index - start);
                foreach (Match token in TOKEN.Matches(text))
                {
                    if (token.Length > LONGEST_TOKEN)
                        continue;

                    for (var tail = 0; tail < token.Length; tail++)
                    {
                        var name = token.Value[tail..];
                        if (char.IsAsciiDigit(name[0]) || RESERVED.Contains(name) || name.StartsWith("oc!", StringComparison.Ordinal)
                            || (name.Length > 1 && name[0] == '-' && char.IsAsciiDigit(name[1])))
                            continue;

                        names.Add(name);
                    }
                }
            }

            start = index + 1;
        }

        return names.Order(StringComparer.Ordinal).ToList();
    }

    #endregion
}
