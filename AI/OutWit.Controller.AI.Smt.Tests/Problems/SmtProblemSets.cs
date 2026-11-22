using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// What the tests of a problem set ask of every row: it is a problem or a refusal, never both; a
/// refusal has a code of the contract, the row's line, and no more of the row than a field's name
/// or a token; a problem travels as text that sanitizes to itself.
/// </summary>
internal static class SmtProblemSets
{
    #region Constants

    /// <summary>A problem with two constants, for rows whose point is not the problem.</summary>
    public const string PROBLEM = "(declare-const x Int)(declare-const y Int)(assert (> x y))";

    /// <summary>The canonical text of <see cref="PROBLEM"/>.</summary>
    public const string CANONICAL = "(declare-const x Int)\n(declare-const y Int)\n(assert (> x y))\n";

    /// <summary>The longest reason a reader or the sanitizer writes is a shape, a few hundred characters.</summary>
    public const int MAX_REASON_CHARACTERS = 420;

    #endregion

    #region Functions

    /// <summary>Reads a set held in a text to its end.</summary>
    /// <param name="jsonl">The set.</param>
    /// <param name="options">The run's options; a new instance when none are given.</param>
    /// <returns>The rows and the summary.</returns>
    public static (List<SmtProblemSetRow> Rows, SmtProblemSetSummary Summary) Read(string jsonl, SmtOptionsData? options = null)
    {
        return Read(Encoding.UTF8.GetBytes(jsonl), options);
    }

    /// <summary>Reads a set held in bytes to its end.</summary>
    /// <param name="bytes">The set.</param>
    /// <param name="options">The run's options; a new instance when none are given.</param>
    /// <returns>The rows and the summary.</returns>
    public static (List<SmtProblemSetRow> Rows, SmtProblemSetSummary Summary) Read(byte[] bytes, SmtOptionsData? options = null)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var reader = new SmtProblemSetReader(stream, options ?? new SmtOptionsData());
        var rows = new List<SmtProblemSetRow>();

        while (reader.TryRead(out var row))
        {
            Hold(row);
            rows.Add(row);
        }

        Assert.That(reader.TryRead(out var after), Is.False, "a set that has ended stays ended");
        Assert.That(after, Is.Null);
        Assert.That(reader.Summary.Rows, Is.EqualTo(rows.Count));
        Assert.That(reader.Summary.Accepted + reader.Summary.Refused, Is.EqualTo(rows.Count));
        Assert.That(reader.Summary.BytesRead, Is.EqualTo(bytes.Length));

        return (rows, reader.Summary);
    }

    /// <summary>Reads a set of one row that must be taken.</summary>
    /// <param name="line">The row.</param>
    /// <param name="options">The run's options.</param>
    /// <returns>The problem.</returns>
    public static SmtProblemData Accept(string line, SmtOptionsData? options = null)
    {
        var row = Read(line, options).Rows.Single();

        Assert.That(row.Refusal, Is.Null, () => $"refused: {row.Refusal?.Code}: {row.Refusal?.Reason}");
        return row.Data!;
    }

    /// <summary>Reads a set of one row that must be refused with a code.</summary>
    /// <param name="line">The row.</param>
    /// <param name="code">The expected code.</param>
    /// <param name="options">The run's options.</param>
    /// <returns>The refusal.</returns>
    public static SmtRefusalData Refuse(string line, string code, SmtOptionsData? options = null)
    {
        var row = Read(line, options).Rows.Single();

        Assert.That(row.Data, Is.Null, "the row was taken");
        Assert.That(row.Refusal!.Code, Is.EqualTo(code), row.Refusal.Reason);
        return row.Refusal;
    }

    /// <summary>A row of the given fields around <see cref="PROBLEM"/>, as one JSON line.</summary>
    /// <param name="fields">The fields after <c>id</c> and <c>smt2</c>, as JSON: <c>"query":"check"</c>.</param>
    /// <returns>The line.</returns>
    public static string Row(string fields = "")
    {
        return "{\"id\":\"a\",\"smt2\":\"" + PROBLEM + "\"" + (fields.Length == 0 ? "" : "," + fields) + "}";
    }

    /// <summary>Holds a row to what every row is.</summary>
    /// <param name="row">The row.</param>
    public static void Hold(SmtProblemSetRow row)
    {
        Assert.That(row.Line, Is.GreaterThanOrEqualTo(1));
        Assert.That(row.Index, Is.GreaterThanOrEqualTo(0));

        if (row.IsAccepted)
        {
            Assert.That(row.Refusal, Is.Null);
            HoldData(row);
            return;
        }

        Assert.That(row.Data, Is.Null);
        Assert.That(row.Problem, Is.Null);
        Assert.That(row.Refusal, Is.Not.Null);

        var refusal = row.Refusal!;
        Assert.That(SmtRefusalCodes.All, Does.Contain(refusal.Code));
        Assert.That(refusal.Row, Is.EqualTo(row.Line));
        Assert.That(refusal.Line, Is.GreaterThanOrEqualTo(0));
        Assert.That(refusal.File.Length, Is.LessThanOrEqualTo(64));
        Assert.That(refusal.Command.Length, Is.LessThanOrEqualTo(64));
        Assert.That(refusal.Reason, Is.Not.Empty);
        Assert.That(refusal.Reason.Length, Is.LessThanOrEqualTo(MAX_REASON_CHARACTERS), refusal.Reason);
    }

    /// <summary>A problem travels as text that is its own canonical text, and its terms as texts that are their own.</summary>
    private static void HoldData(SmtProblemSetRow row)
    {
        var data = row.Data!;
        var problem = row.Problem!;

        Assert.That(data.Index, Is.EqualTo(row.Index));
        Assert.That(data.Id, Is.Not.Empty);
        Assert.That(data.Text, Is.EqualTo(problem.Canonical));
        Assert.That(data.Hash, Is.EqualTo(problem.Hash));

        var again = SmtSanitizer.Sanitize(data.Text);
        Assert.That(again.Problem?.Hash, Is.EqualTo(data.Hash), "a node that sanitizes the text again gets the same problem");

        foreach (var term in data.Values.Append(data.Goal).Where(text => text.Length > 0).Concat(data.Project))
            Assert.That(SmtSanitizer.SanitizeTerm(term).Term?.ToString(), Is.EqualTo(term), "a term travels as its own canonical text");

        // A problem carries the fields of its own query and none of another's.
        if (data.Query != SmtQuery.Check)
            Assert.That(data.Values, Is.Empty);

        if (data.Query == SmtQuery.Enumerate)
        {
            Assert.That(data.Project, Is.Not.Empty);
            Assert.That(data.MaxModels, Is.InRange(1, SmtOptionsRules.MAX_MODELS));
        }
        else
        {
            Assert.That(data.Project, Is.Empty);
            Assert.That(data.MaxModels, Is.Zero);
        }

        if (data.Query == SmtQuery.Prove)
        {
            Assert.That(data.Goal, Is.Not.Empty);
            Assert.That(data.Expected, Is.EqualTo(SmtProblemStatus.None));
        }
        else
        {
            Assert.That(data.Goal, Is.Empty);
        }
    }

    #endregion
}
