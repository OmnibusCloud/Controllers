using System.Collections.Concurrent;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// What the solver does with a problem that declares a name it has built in, asked of the solver
/// itself, name by name. The names are every name the pinned module reads as an operator or a
/// constant of its own under the logic <c>ALL</c> or with no logic, 313 of them, each with what
/// the solver did with it, in <c>Fixtures/Solver/builtin-names.tsv</c>: they were found by giving
/// the solver every string of the module that could be a symbol, bare and applied, and keeping
/// those it did not call unknown.
/// <para>
/// Each question is a problem whose answer differs between the two readings. A constant of the
/// name, of sort Real, said to be 11.5 and then 12.5: the problem's constant can be either, the
/// solver's own (<c>pi</c>) neither. A function of the name, declared with a signature the
/// solver's operator takes, said to give 11 and then 12 at one point: the problem's function can
/// give either, the solver's operator gives one value. Both answers <c>sat</c> is the
/// declaration; an <c>unsat</c> is the solver's own operator; an error is an error. The texts go
/// to the solver as they are (<see cref="SmtRawSolver"/>): the sanitizer refuses some of them,
/// and it is what the solver does with exactly those that is asked.
/// </para>
/// <para>
/// The names whose function the solver reads as its own operator are the ones the sanitizer
/// refuses (<see cref="SmtRefusalCodes.BUILTIN_NAME"/>), and the solver does so in every position
/// a problem can declare a function and only under <c>ALL</c> or without a logic: both are held
/// here, with the sanitizer's verdict on every such problem beside the solver's.
/// </para>
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtSolverBuiltinNameTests
{
    #region Constants

    private const string FIXTURE = "Fixtures/Solver/builtin-names.tsv";
    private const string ALL = "(set-logic ALL)";
    private const string DECLARATION = "declaration";
    private const string BUILTIN = "builtin";
    private const string ERROR = "error";
    private const string NONE = "-";
    private const int NAMES = 313;
    private const int PARALLEL = 4;

    /// <summary>The logics under which the solver reads its own operator: none, and <c>ALL</c>.</summary>
    private static readonly string[] OVERRIDING_LOGICS = ["", ALL];

    /// <summary>Logics with reals and functions, where the same names are the problem's.</summary>
    private static readonly string[] OTHER_LOGICS = ["(set-logic QF_UFNRA)", "(set-logic UFNIRA)", "(set-logic AUFNIRA)", "(set-logic QF_UFLRA)"];

    /// <summary>Logics of those in which the solver takes recursive definitions at all (in the others it refuses every one).</summary>
    private static readonly string[] RECURSIVE_LOGICS = ["(set-logic UFNIRA)", "(set-logic UFNRA)"];

    /// <summary>A logic with datatypes and reals, where a constructor of such a name is the problem's.</summary>
    private const string DATATYPE_LOGIC = "(set-logic QF_UFDTNIRA)";

    #endregion

    #region Fields

    private List<(string Name, string Constant, string Parameters, string Arguments, string Result, string First, string Second, string Function)> m_rows = null!;
    private Dictionary<string, (string Constant, string Function)> m_read = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        m_rows = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, FIXTURE.Replace('/', Path.DirectorySeparatorChar)))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(cells => (cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7]))
            .ToList();

        var read = new ConcurrentDictionary<string, (string Constant, string Function)>(StringComparer.Ordinal);
        Parallel.ForEach(m_rows, new ParallelOptions { MaxDegreeOfParallelism = PARALLEL }, row =>
        {
            var constant = Reading(
                $"{ALL}(declare-fun {row.Name} () Real)(assert (= {row.Name} 11.5))",
                $"{ALL}(declare-fun {row.Name} () Real)(assert (= {row.Name} 12.5))");
            var function = row.Function == NONE
                ? NONE
                : Reading(
                    $"{ALL}(declare-fun {row.Name} ({row.Parameters}) {row.Result})(assert (= ({row.Name} {row.Arguments}) {row.First}))",
                    $"{ALL}(declare-fun {row.Name} ({row.Parameters}) {row.Result})(assert (= ({row.Name} {row.Arguments}) {row.Second}))");
            read[row.Name] = (constant, function);
        });

        m_read = new Dictionary<string, (string Constant, string Function)>(read, StringComparer.Ordinal);
        TestContext.Progress.WriteLine(
            $"{m_rows.Count} names the solver has built in: constants {Counts(m_read.Values.Select(value => value.Constant))}; functions {Counts(m_read.Values.Select(value => value.Function))}");
    }

    #endregion

    #region Builtin Name Tests

    [Test]
    public void EveryNameTheSolverHasBuiltInIsReadAsRecordedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_rows, Has.Count.EqualTo(NAMES));
            Assert.That(m_rows.Select(row => row.Name), Is.Unique);
            foreach (var row in m_rows)
            {
                var read = m_read[row.Name];
                Assert.That(read.Constant, Is.EqualTo(row.Constant), $"a constant named '{row.Name}'");
                Assert.That(read.Function, Is.EqualTo(row.Function), $"a function named '{row.Name}' of ({row.Parameters}) {row.Result}");
            }
        });
    }

    [Test]
    public void TheSanitizerRefusesTheNamesWhoseFunctionTheSolverReadsAsItsOwnTest()
    {
        var overriding = m_rows.Where(row => row.Function == BUILTIN).Select(row => row.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(SmtBuiltinNames.OVERRIDING, Is.EquivalentTo(overriding));

            // A constant of such a name is the problem's: those problems are taken.
            foreach (var row in m_rows.Where(row => row.Function == BUILTIN))
                Assert.That(row.Constant, Is.EqualTo(DECLARATION), row.Name);

            // Every other function of a name the solver has built in is an error to the solver, or no fitting signature was found.
            Assert.That(m_rows.Where(row => row.Function != BUILTIN).Select(row => row.Function), Is.All.AnyOf(ERROR, NONE));
        });
    }

    [Test]
    public void TheSolverReadsItsOwnOperatorInEveryPositionOnlyUnderAllOrWithoutALogicTest()
    {
        var cases = new List<(string Name, string Position, string Logic, string First, string Second)>();
        foreach (var row in m_rows.Where(row => row.Function == BUILTIN))
        {
            var sorts = row.Parameters.Split(' ');
            var parameters = string.Join(" ", sorts.Select((sort, index) => $"(a{index} {sort})"));
            var fields = string.Join(" ", sorts.Select((sort, index) => $"(s{index} {sort})"));
            var application = $"({row.Name} {row.Arguments})";
            foreach (var logic in OVERRIDING_LOGICS.Concat(OTHER_LOGICS))
            {
                cases.Add((row.Name, "declare-fun", logic,
                    $"{logic}(declare-fun {row.Name} ({row.Parameters}) {row.Result})(assert (= {application} {row.First}))",
                    $"{logic}(declare-fun {row.Name} ({row.Parameters}) {row.Result})(assert (= {application} {row.Second}))"));

                // The solver reports an error for this one where it has the operator; the
                // sanitizer refuses it with the others, so that the rule is one rule.
                cases.Add((row.Name, "define-fun", logic,
                    $"{logic}(define-fun {row.Name} ({parameters}) {row.Result} {row.First})(assert (= {application} {row.First}))",
                    $"{logic}(define-fun {row.Name} ({parameters}) {row.Result} {row.Second})(assert (= {application} {row.Second}))"));
            }

            foreach (var logic in OVERRIDING_LOGICS.Concat(RECURSIVE_LOGICS))
            {
                cases.Add((row.Name, "define-fun-rec", logic,
                    $"{logic}(define-fun-rec {row.Name} ({parameters}) {row.Result} {row.First})(assert (= {application} {row.First}))",
                    $"{logic}(define-fun-rec {row.Name} ({parameters}) {row.Result} {row.Second})(assert (= {application} {row.Second}))"));
                cases.Add((row.Name, "define-funs-rec", logic,
                    $"{logic}(define-funs-rec (({row.Name} ({parameters}) {row.Result})) ({row.First}))(assert (= {application} {row.First}))",
                    $"{logic}(define-funs-rec (({row.Name} ({parameters}) {row.Result})) ({row.Second}))(assert (= {application} {row.Second}))"));
            }

            // A constructor's value is of its datatype: where the solver reads its own operator,
            // the value is a Real, and the problem does not type.
            foreach (var logic in OVERRIDING_LOGICS.Append(DATATYPE_LOGIC))
            {
                var text = $"{logic}(declare-datatype D (({row.Name} {fields}) (other)))(declare-const d D)(assert (= d {application}))";
                cases.Add((row.Name, "constructor", logic, text, text));
            }
        }

        var outcomes = new ConcurrentBag<string>();
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = PARALLEL }, item =>
        {
            var solver = Reading(item.First, item.Second);
            var overrides = OVERRIDING_LOGICS.Contains(item.Logic);
            var expected = (overrides, item.Position) switch
            {
                (false, _) => DECLARATION,
                (true, "constructor" or "define-fun") => ERROR,
                _ => BUILTIN
            };

            var sanitized = SmtSanitizer.Sanitize(item.First);
            var sanitizer = sanitized.IsAccepted ? "taken" : sanitized.Refusal!.Code;
            var refused = overrides ? SmtRefusalCodes.BUILTIN_NAME : "taken";
            if (solver != expected || sanitizer != refused)
                outcomes.Add($"'{item.Name}' as {item.Position} under '{item.Logic}': the solver reads {solver} (expected {expected}), the sanitizer says {sanitizer} (expected {refused})");
        });

        TestContext.Out.WriteLine($"{cases.Count} problems, each asked twice");
        Assert.That(outcomes, Is.Empty);
    }

    #endregion

    #region Tools

    /// <summary>
    /// What the solver makes of the two problems of one question: both satisfiable is the
    /// problem's declaration, an unsatisfiable one is the solver's own operator.
    /// </summary>
    private static string Reading(string first, string second)
    {
        var one = SmtRawSolver.Ask(first);
        var two = SmtRawSolver.Ask(second);
        if (IsError(one) || IsError(two))
            return ERROR;

        if (one == "sat" && two == "sat")
            return DECLARATION;

        return one is "sat" or "unsat" && two is "sat" or "unsat" ? BUILTIN : $"other ({one} / {two})";
    }

    private static bool IsError(string answer)
    {
        return answer.Contains("(error", StringComparison.Ordinal);
    }

    private static string Counts(IEnumerable<string> readings)
    {
        return string.Join(", ", readings.GroupBy(reading => reading).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Count()} {group.Key}"));
    }

    #endregion
}
