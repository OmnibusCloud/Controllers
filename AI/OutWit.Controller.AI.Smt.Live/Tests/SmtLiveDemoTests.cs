using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Tests;

/// <summary>
/// The demo kits held to what their <c>EXPECTED.md</c> says, without a solver. The eight-queens
/// set: both rows are taken by the model package's reader; the 92 placements of eight queens, and
/// no other placement of one queen a row, satisfy the first problem by the model evaluator; the
/// given cells of the Latin square have exactly one completion, which satisfies the second. The
/// known-answers kit: its table is the list's and the solver slice test's.
/// </summary>
[TestFixture]
public sealed class SmtLiveDemoTests
{
    #region Constants

    private const string QUEENS_SET = "Demo/eight-queens.jsonl";
    private const string QUEENS_EXPECTED = "AI/OutWit.Controller.AI.Smt.Live/demo/02-eight-queens/EXPECTED.md";
    private const string KNOWN_EXPECTED = "AI/OutWit.Controller.AI.Smt.Live/demo/01-known-answers/EXPECTED.md";
    private const string SLICE_LIST = "AI/OutWit.Controller.AI.Smt.Tests/Slice/fetch-list.tsv";
    private const string SLICE_TEST = "AI/OutWit.Controller.AI.Smt.Tests/Solver/SmtSolverSliceTests.cs";

    private const int QUEENS = 8;
    private const int SOLUTIONS = 92;
    private const int ORDER = 5;

    /// <summary>A placement of eight queens no two of which attack each other.</summary>
    private static readonly int[] SOLUTION = [0, 4, 7, 5, 2, 6, 1, 3];

    /// <summary>The six cells the Latin square gives: x{row}{column} from 1.</summary>
    private static readonly Dictionary<string, int> GIVEN = new(StringComparer.Ordinal) { ["x25"] = 4, ["x33"] = 4, ["x35"] = 5, ["x42"] = 1, ["x44"] = 3, ["x52"] = 3 };

    #endregion

    #region Eight Queens Tests

    [Test]
    public void TheEightQueensSetIsTakenByTheReaderTest()
    {
        var rows = Rows();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.All(row => row.IsAccepted), Is.True, string.Join("; ", rows.Select(row => row.Refusal?.Reason)));
            Assert.That(rows.Select(row => row.Data!.Id), Is.EqualTo(new[] { "eight-queens", "latin-5x5" }));
            Assert.That(rows.Select(row => row.Data!.Query), Is.All.EqualTo(SmtQuery.Enumerate));
            Assert.That(rows.Select(row => row.Data!.Project.Count), Is.EqualTo(new[] { QUEENS, ORDER * ORDER }));
            Assert.That(rows.Select(row => row.Data!.MaxModels), Is.EqualTo(new[] { 100, 2 }));
            Assert.That(rows.Select(row => row.Data!.Expected), Is.All.EqualTo(SmtProblemStatus.Sat));
        });
    }

    [Test]
    public void TheEightQueensSetIsTheBytesItsExpectedNamesTest()
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, QUEENS_SET));
        var expected = File.ReadAllText(Path.Combine(SmtLiveFixtures.RepositoryRoot(), QUEENS_EXPECTED));

        Assert.That(bytes, Does.Not.Contain((byte)'\r'), "LF line ends, as the attributes hold them on every checkout");
        Assert.That(expected, Does.Contain($"`{Convert.ToHexStringLower(SHA256.HashData(bytes))}`"));
        Assert.That(expected, Does.Contain($"2 rows, {bytes.Length:N0} bytes"));
    }

    [Test]
    public void EveryPlacementOfEightQueensIsAModelOfTheFirstProblemAndNoOtherIsTest()
    {
        var validator = new SmtModelValidator(Rows()[0].Problem!);
        var models = 0;
        var others = 0;

        foreach (var placement in Permutations(Enumerable.Range(0, QUEENS).ToArray()))
        {
            var validation = Validate(validator, placement.Select((column, row) => ($"q{row}", column)));
            if (validation == SmtValidationClass.Validated)
            {
                models++;
                Assert.That(Attacks(placement), Is.False, string.Join(" ", placement));
            }
            else
            {
                others++;
                Assert.That(validation, Is.EqualTo(SmtValidationClass.Failed), string.Join(" ", placement));
            }
        }

        Assert.That(models, Is.EqualTo(SOLUTIONS));
        Assert.That(others, Is.EqualTo(40_320 - SOLUTIONS));
    }

    [Test]
    public void APlacementOffTheBoardOrTwoQueensOnAColumnAreNoModelTest()
    {
        var validator = new SmtModelValidator(Rows()[0].Problem!);

        Assert.Multiple(() =>
        {
            Assert.That(Validate(validator, SOLUTION.Select((column, row) => ($"q{row}", column))), Is.EqualTo(SmtValidationClass.Validated));
            Assert.That(Validate(validator, SOLUTION.Select((column, row) => ($"q{row}", column + 1))), Is.EqualTo(SmtValidationClass.Failed), "shifted right: q2 is 8");
            Assert.That(Validate(validator, SOLUTION.Select((column, row) => ($"q{row}", column - 1))), Is.EqualTo(SmtValidationClass.Failed), "shifted left: q0 is -1");
            Assert.That(Validate(validator, SOLUTION.Select((column, row) => ($"q{row}", row == 1 ? SOLUTION[0] : column))), Is.EqualTo(SmtValidationClass.Failed), "two on a column");
        });
    }

    #endregion

    #region Latin Square Tests

    [Test]
    public void TheLatinSquareGivesTheCellsItsKitNamesTest()
    {
        var given = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var command in Rows()[1].Problem!.Commands.Where(command => command.Head == "assert"))
        {
            var term = command.Items[1];
            if (term.Head == "=" && term.Items.Count == 3 && term.Items[1].Kind == SmtExpressionKind.Symbol && term.Items[2].Kind == SmtExpressionKind.Numeral)
                given[term.Items[1].Text] = int.Parse(term.Items[2].Text);
        }

        Assert.That(given, Is.EquivalentTo(GIVEN));
    }

    [Test]
    public void TheGivenCellsHaveExactlyOneCompletionAndItIsAModelTest()
    {
        var completions = Completions();
        Assert.That(completions, Has.Count.EqualTo(1));

        var square = completions[0];
        TestContext.Out.WriteLine(string.Join("\n", Enumerable.Range(0, ORDER).Select(row => string.Join(" ", Enumerable.Range(0, ORDER).Select(column => square[row, column])))));

        var validator = new SmtModelValidator(Rows()[1].Problem!);
        Assert.That(Validate(validator, Cells(square)), Is.EqualTo(SmtValidationClass.Validated));

        (square[0, 0], square[0, 1]) = (square[0, 1], square[0, 0]);
        Assert.That(Validate(validator, Cells(square)), Is.EqualTo(SmtValidationClass.Failed), "two cells of a row swapped: their columns repeat");
    }

    #endregion

    #region Known Answers Tests

    [Test]
    public void TheKnownAnswersTableIsTheListsAndTheSliceTestsTest()
    {
        var root = SmtLiveFixtures.RepositoryRoot();
        var expected = File.ReadAllText(Path.Combine(root, KNOWN_EXPECTED));
        var table = Regex.Matches(expected, @"^\| (QF_\w+) \| (\d+) \| (\d+) \| (\d+) \| (\d+) / (\d+) / (\d+) \|$", RegexOptions.Multiline)
            .ToDictionary(match => match.Groups[1].Value, match => string.Join(" ", Enumerable.Range(2, 6).Select(group => match.Groups[group].Value)));

        var lines = File.ReadAllLines(Path.Combine(root, SLICE_LIST)).Skip(1).Where(line => line.Length > 0).Select(line => line.Split('\t')).ToList();
        var classes = Regex.Matches(File.ReadAllText(Path.Combine(root, SLICE_TEST)), @"\[""(\w+)""\]\s*=\s*\((\d+),\s*(\d+),\s*(\d+)\)")
            .ToDictionary(match => match.Groups[1].Value, match => $"{match.Groups[2].Value} {match.Groups[3].Value} {match.Groups[4].Value}");
        var listed = lines.GroupBy(cells => cells[1]).ToDictionary(group => group.Key,
            group => $"{group.Count()} {group.Count(cells => cells[5] == "sat")} {group.Count(cells => cells[5] == "unsat")} {classes[group.Key]}");

        Assert.That(table, Is.EquivalentTo(listed));
        Assert.That(expected, Does.Contain($"sat {lines.Count(cells => cells[5] == "sat")}, unsat {lines.Count(cells => cells[5] == "unsat")}, unknown 0"));
    }

    #endregion

    #region Tools

    private static List<SmtProblemSetRow> Rows()
    {
        using var stream = File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, QUEENS_SET));
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var rows = new List<SmtProblemSetRow>();
        while (reader.TryRead(out var row))
            rows.Add(row);

        return rows;
    }

    private static SmtValidationClass Validate(SmtModelValidator validator, IEnumerable<(string Name, int Value)> values)
    {
        var model = new StringBuilder("(");
        foreach (var (name, value) in values)
            model.Append($"(define-fun {name} () Int {(value < 0 ? $"(- {-value})" : value.ToString())})");

        model.Append(')');
        return validator.Validate(SmtModel.Read(model.ToString(), null, SmtReaderLimits.Unbounded), SmtEvaluationBudget.Default).Class;
    }

    private static IEnumerable<int[]> Permutations(int[] items, int from = 0)
    {
        if (from == items.Length)
        {
            yield return [.. items];
            yield break;
        }

        for (var index = from; index < items.Length; index++)
        {
            (items[from], items[index]) = (items[index], items[from]);
            foreach (var permutation in Permutations(items, from + 1))
                yield return permutation;

            (items[from], items[index]) = (items[index], items[from]);
        }
    }

    private static bool Attacks(int[] placement)
    {
        for (var a = 0; a < placement.Length; a++)
        {
            for (var b = a + 1; b < placement.Length; b++)
            {
                if (Math.Abs(placement[a] - placement[b]) == b - a)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Every Latin square of the order with the given cells, by a search of its own.</summary>
    private static List<int[,]> Completions()
    {
        var square = new int[ORDER, ORDER];
        foreach (var (name, value) in GIVEN)
            square[name[1] - '1', name[2] - '1'] = value;

        var found = new List<int[,]>();
        Fill(square, 0, found);
        return found;
    }

    private static void Fill(int[,] square, int cell, List<int[,]> found)
    {
        if (cell == ORDER * ORDER)
        {
            found.Add((int[,])square.Clone());
            return;
        }

        int row = cell / ORDER, column = cell % ORDER;
        if (square[row, column] != 0)
        {
            Fill(square, cell + 1, found);
            return;
        }

        for (var value = 1; value <= ORDER; value++)
        {
            if (Enumerable.Range(0, ORDER).Any(other => square[row, other] == value || square[other, column] == value))
                continue;

            square[row, column] = value;
            Fill(square, cell + 1, found);
            square[row, column] = 0;
        }
    }

    private static IEnumerable<(string Name, int Value)> Cells(int[,] square)
    {
        for (var row = 0; row < ORDER; row++)
        {
            for (var column = 0; column < ORDER; column++)
                yield return ($"x{row + 1}{column + 1}", square[row, column]);
        }
    }

    #endregion
}
