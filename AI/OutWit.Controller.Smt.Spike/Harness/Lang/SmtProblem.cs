using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.Smt.Spike.Lang;

/// <summary>
/// One SMT-LIB problem as the controller would hand it to the solver: the declarative commands of
/// the file in their order, re-written from tokens, with the commands the query decides on
/// (check-sat, get-*, exit, set-option, echo) stripped. Enough of the specification's sanitizer
/// (§3) for the spike; the refusal table and the shape limits are SM-1's.
/// </summary>
internal sealed class SmtProblem
{
    #region Constants

    private static readonly HashSet<string> KEPT =
    [
        "set-logic", "set-info", "declare-sort", "define-sort", "declare-const", "declare-fun", "define-fun",
        "define-fun-rec", "define-funs-rec", "declare-datatype", "declare-datatypes", "assert"
    ];

    private static readonly HashSet<string> STRIPPED =
    [
        "check-sat", "get-model", "get-value", "get-info", "get-assignment", "get-option", "exit", "set-option", "echo"
    ];

    #endregion

    #region Constructors

    private SmtProblem(List<SExpr> commands, Dictionary<string, int> stripped)
    {
        Commands = commands;
        Stripped = stripped;

        var builder = new StringBuilder();
        foreach (var command in commands)
        {
            command.WriteTo(builder);
            builder.Append('\n');
        }

        Canonical = builder.ToString();
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical))).ToLowerInvariant();

        foreach (var command in commands)
            Collect(command);
    }

    #endregion

    #region Functions

    /// <summary>Reads a problem from SMT-LIB text.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The canonical problem.</returns>
    /// <exception cref="FormatException">The text is not well-formed, or it holds a command the spike cannot run.</exception>
    public static SmtProblem Parse(string text)
    {
        var kept = new List<SExpr>();
        var stripped = new Dictionary<string, int>();
        foreach (var form in SmtReader.ReadAll(text))
        {
            var head = form.Head ?? throw new FormatException($"line {form.Line}: a top-level form that is no command");
            if (KEPT.Contains(head))
                kept.Add(form);
            else if (STRIPPED.Contains(head))
                stripped[head] = stripped.GetValueOrDefault(head) + 1;
            else
                throw new FormatException($"line {form.Line}: command not allowed: {head}");
        }

        return new SmtProblem(kept, stripped);
    }

    private void Collect(SExpr command)
    {
        var items = command.Items;
        switch (command.Head)
        {
            case "set-logic" when items.Count == 2:
                Logic = items[1].Text;
                break;
            case "set-info" when items.Count == 3 && items[1].Text == ":status":
                Status = items[2].Text;
                break;
            case "declare-const" when items.Count == 3:
                Constants.Add((items[1].Text, items[1], items[2]));
                break;
            case "declare-fun" when items.Count == 4:
                if (items[2].IsList && items[2].Items.Count == 0)
                    Constants.Add((items[1].Text, items[1], items[3]));
                else
                    Functions.Add(items[1].Text);
                break;
            case "assert":
                AssertionCount++;
                break;
        }
    }

    #endregion

    #region Properties

    /// <summary>The kept commands, in the file's order.</summary>
    public IReadOnlyList<SExpr> Commands { get; }

    /// <summary>The stripped commands, counted by name.</summary>
    public IReadOnlyDictionary<string, int> Stripped { get; }

    /// <summary>The problem as the solver sees it: one command per line.</summary>
    public string Canonical { get; }

    /// <summary>SHA-256 of <see cref="Canonical"/>, hex.</summary>
    public string Hash { get; }

    /// <summary>The logic of set-logic, or null.</summary>
    public string? Logic { get; private set; }

    /// <summary>The :status of set-info (the expected verdict), or null.</summary>
    public string? Status { get; private set; }

    /// <summary>Every declared constant (a declare-const, or a declare-fun of no arguments) with its sort.</summary>
    public List<(string Name, SExpr Symbol, SExpr Sort)> Constants { get; } = [];

    /// <summary>Every declared function of one argument or more.</summary>
    public List<string> Functions { get; } = [];

    /// <summary>The number of assert commands.</summary>
    public int AssertionCount { get; private set; }

    #endregion
}
