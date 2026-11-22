using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// What one line of a problem set holds, field by field, before the fields are held to each
/// other: the values that were read, which fields were given, and the first thing that is wrong
/// with one of them, in the order of the line.
/// </summary>
internal sealed class SmtProblemSetRowDraft
{
    #region Functions

    /// <summary>
    /// Notes what is wrong with a field. The first fault of a line is the one kept.
    /// </summary>
    /// <param name="code">The code (<see cref="SmtRefusalCodes"/>).</param>
    /// <param name="reason">What is wrong, in words.</param>
    public void Refuse(string code, string reason)
    {
        Fault ??= SmtRefusals.OfRow(code, reason);
    }

    /// <summary>
    /// Whether the row gave a field, with a good value or a bad one.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>True when the line names it.</returns>
    public bool Has(SmtProblemSetField field)
    {
        return (Seen & field) != 0;
    }

    #endregion

    #region Properties

    /// <summary>The fields the line names.</summary>
    public SmtProblemSetField Seen { get; set; }

    /// <summary>Whether the line is no row at all: not UTF-8, or not one JSON object. Nothing of it is readable then, its id included.</summary>
    public bool IsMalformed { get; set; }

    /// <summary>The first thing wrong with the line or with one of its fields; null when every field that was given reads.</summary>
    public SmtRefusalData? Fault { get; set; }

    /// <summary>The id, when the line gives one once and it is an id; null otherwise.</summary>
    public string? Id { get; set; }

    /// <summary>The text of the problem.</summary>
    public string? Smt2 { get; set; }

    /// <summary>The query the row names.</summary>
    public SmtQuery? Query { get; set; }

    /// <summary>The texts of the terms whose values are asked.</summary>
    public List<string>? Values { get; set; }

    /// <summary>The texts of the names to project on.</summary>
    public List<string>? Project { get; set; }

    /// <summary>The number of models the row names.</summary>
    public int? MaxModels { get; set; }

    /// <summary>The text of the goal.</summary>
    public string? Goal { get; set; }

    /// <summary>The verdict the row expects.</summary>
    public SmtProblemStatus? Expected { get; set; }

    /// <summary>The tags.</summary>
    public string? Tags { get; set; }

    #endregion
}
