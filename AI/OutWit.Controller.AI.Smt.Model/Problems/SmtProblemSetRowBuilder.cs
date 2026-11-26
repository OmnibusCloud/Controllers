using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// Makes a row of the fields of a line: holds the fields to each other and to the set, sanitizes
/// the problem and the terms beside it, and gives the problem as it travels, or the refusal.
/// What is looked at, in this order:
/// <list type="number">
/// <item>the line is a row at all, and every field that was given reads (the first fault, in the order of the line);</item>
/// <item>the row has an id, and no earlier row has it;</item>
/// <item>the row has a problem;</item>
/// <item>every field belongs to the row's query (the row's own, else the run's), and the query has the fields it needs;</item>
/// <item>the problem is one the sanitizer takes;</item>
/// <item>the terms and the names beside it are terms the sanitizer takes, and the names are constants the problem declares (<see cref="SmtProjection"/>).</item>
/// </list>
/// The first row with an id keeps it, whatever becomes of the row: a later row with the same id
/// is a duplicate even when the first was refused.
/// </summary>
internal sealed class SmtProblemSetRowBuilder
{
    #region Constants

    /// <summary>The fields that belong to some queries only, in the order they are looked at, each with the words for the rows it belongs to.</summary>
    private static readonly (SmtProblemSetField Field, string Rows)[] OWNED =
    [
        (SmtProblemSetField.Values, "a check row"),
        (SmtProblemSetField.Project, "an enumerate row"),
        (SmtProblemSetField.MaxModels, "an enumerate row"),
        (SmtProblemSetField.Goal, "a prove row"),
        (SmtProblemSetField.Expected, "a check or an enumerate row")
    ];

    #endregion

    #region Fields

    private readonly SmtOptionsData m_options;
    private readonly SmtProblemSetIds m_ids = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the maker of a set's rows.
    /// </summary>
    /// <param name="options">The run's options, with their defaults filled in.</param>
    public SmtProblemSetRowBuilder(SmtOptionsData options)
    {
        m_options = options;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes the row of one line.
    /// </summary>
    /// <param name="draft">The fields of the line.</param>
    /// <param name="line">The line's number in the set, from 1.</param>
    /// <param name="index">The row's ordinal among the rows of the set, from 0.</param>
    /// <returns>The row: a problem, or a refusal with the row's line and its id.</returns>
    public SmtProblemSetRow Build(SmtProblemSetRowDraft draft, int line, int index)
    {
        var refusal = Check(draft, line, out var data, out var problem);
        if (refusal == null && data != null && problem != null)
        {
            data.Index = index;
            return new SmtProblemSetRow(line, index, data, problem);
        }

        return Refused(refusal ?? SmtRefusals.OfRow(SmtRefusalCodes.ROW_MALFORMED, "the row could not be read"), line, index, draft.Id);
    }

    /// <summary>
    /// Makes the row of a line that was refused before its fields were read.
    /// </summary>
    /// <param name="refusal">Why.</param>
    /// <param name="line">The line's number in the set, from 1.</param>
    /// <param name="index">The row's ordinal among the rows of the set, from 0.</param>
    /// <param name="id">The row's id when it has a readable one.</param>
    /// <returns>The refused row.</returns>
    public static SmtProblemSetRow Refused(SmtRefusalData refusal, int line, int index, string? id = null)
    {
        refusal.Row = line;
        refusal.File = id ?? "";
        return new SmtProblemSetRow(line, index, refusal);
    }

    private SmtRefusalData? Check(SmtProblemSetRowDraft draft, int line, out SmtProblemData? data, out SmtProblem? problem)
    {
        data = null;
        problem = null;

        if (draft.IsMalformed)
            return draft.Fault;

        var earlier = 0;
        var isTaken = draft.Id != null && !m_ids.TryTake(draft.Id, line, out earlier);

        if (draft.Fault != null)
            return draft.Fault;

        if (draft.Id == null)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_MISSING, "'id' is missing: a row names its problem");

        if (isTaken)
            return SmtRefusals.OfRow(SmtRefusalCodes.ID_DUPLICATE, $"the id is the id of the row on line {earlier}: an id names one row of a set");

        if (draft.Smt2 == null)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_MISSING, "'smt2' is missing: a row holds the text of its problem");

        var query = draft.Query ?? m_options.Query;
        var misplaced = Misplaced(draft, query) ?? Missing(draft, query);
        if (misplaced != null)
            return misplaced;

        var sanitized = SmtSanitizer.Sanitize(draft.Smt2);
        if (!sanitized.IsAccepted)
            return sanitized.Refusal;

        problem = sanitized.Problem;
        data = new SmtProblemData
        {
            Id = draft.Id,
            Query = query,
            Text = problem.Canonical,
            Hash = problem.Hash,
            Expected = query == SmtQuery.Prove ? SmtProblemStatus.None : draft.Expected ?? problem.Status,
            Tags = draft.Tags ?? ""
        };

        return query switch
        {
            SmtQuery.Check => Terms(draft.Values, data.Values),
            SmtQuery.Enumerate => Names(draft, problem, data),
            _ => Goal(draft.Goal!, data)
        };
    }

    /// <summary>A field of another query than the row's.</summary>
    private static SmtRefusalData? Misplaced(SmtProblemSetRowDraft draft, SmtQuery query)
    {
        foreach (var (field, rows) in OWNED)
        {
            if (!draft.Has(field) || Owns(query, field))
                continue;

            var whose = draft.Query == null ? ", the run's default" : "";
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_NOT_ALLOWED,
                $"'{SmtProblemSetRowParser.NameOf(field)}' is a field of {rows}; the query of this row is {Name(query)}{whose}");
        }

        return null;
    }

    private static bool Owns(SmtQuery query, SmtProblemSetField field)
    {
        return field switch
        {
            SmtProblemSetField.Values => query == SmtQuery.Check,
            SmtProblemSetField.Project or SmtProblemSetField.MaxModels => query == SmtQuery.Enumerate,
            SmtProblemSetField.Goal => query == SmtQuery.Prove,
            SmtProblemSetField.Expected => query != SmtQuery.Prove,
            _ => true
        };
    }

    /// <summary>A field the row's query cannot do without.</summary>
    private static SmtRefusalData? Missing(SmtProblemSetRowDraft draft, SmtQuery query)
    {
        if (query == SmtQuery.Enumerate && draft.Project == null)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_MISSING, "'project' is missing: an enumerate row names the constants its models are told apart by");

        if (query == SmtQuery.Prove && draft.Goal == null)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_MISSING, "'goal' is missing: a prove row names what is to be proved");

        return null;
    }

    private static SmtRefusalData? Terms(List<string>? texts, List<string> values)
    {
        if (texts == null)
            return null;

        for (var index = 0; index < texts.Count; index++)
        {
            var term = SmtSanitizer.SanitizeTerm(texts[index]);
            if (!term.IsAccepted)
                return In(term.Refusal!, $"values[{index}]");

            values.Add(term.Term.ToString());
        }

        return null;
    }

    private static SmtRefusalData? Goal(string text, SmtProblemData data)
    {
        var term = SmtSanitizer.SanitizeTerm(text);
        if (!term.IsAccepted)
            return In(term.Refusal!, "goal");

        data.Goal = term.Term.ToString();
        return null;
    }

    /// <summary>
    /// The constants an enumeration is projected on, by the check a node makes of them again
    /// (<see cref="SmtProjection"/>): each a symbol that names a constant the problem declares,
    /// none twice. A name is kept as the problem declares it.
    /// </summary>
    private SmtRefusalData? Names(SmtProblemSetRowDraft draft, SmtProblem problem, SmtProblemData data)
    {
        var refusal = SmtProjection.Take(problem, draft.Project!, out var constants);
        if (refusal != null)
            return refusal;

        foreach (var constant in constants)
            data.Project.Add(constant.Symbol.ToString());

        data.MaxModels = draft.MaxModels ?? m_options.MaxModels;
        return null;
    }

    /// <summary>The refusal of a term, with the field the term stands in said first.</summary>
    private static SmtRefusalData In(SmtRefusalData refusal, string field)
    {
        refusal.Reason = $"'{field}': {refusal.Reason}";
        return refusal;
    }

    private static string Name(SmtQuery query)
    {
        return query switch
        {
            SmtQuery.Check => "check",
            SmtQuery.Enumerate => "enumerate",
            _ => "prove"
        };
    }

    #endregion
}
