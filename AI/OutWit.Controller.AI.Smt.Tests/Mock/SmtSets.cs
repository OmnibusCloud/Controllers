using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Problem sets as a lab writes them: one JSON line a row (<c>docs/smt-problems.schema.json</c> of
/// the model package). A row is written from a problem as it travels (its canonical text is a
/// problem of its own), so a set made of the solver's test problems gives those problems, with the
/// same hashes, at the indices the set puts them.
/// </summary>
internal static class SmtSets
{
    #region Functions

    /// <summary>The line of a problem: its id, query, text, and the fields of its query.</summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The line, without its line feed.</returns>
    public static string Line(SmtProblemData problem)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", problem.Id);
            writer.WriteString("query", Query(problem.Query));
            writer.WriteString("smt2", problem.Text);

            switch (problem.Query)
            {
                case SmtQuery.Check when problem.Values.Count > 0:
                    WriteArray(writer, "values", problem.Values);
                    break;
                case SmtQuery.Enumerate:
                    WriteArray(writer, "project", problem.Project);
                    if (problem.MaxModels > 0)
                        writer.WriteNumber("max_models", problem.MaxModels);
                    break;
                case SmtQuery.Prove:
                    writer.WriteString("goal", problem.Goal);
                    break;
            }

            if (problem.Tags.Length > 0)
                writer.WriteString("tags", problem.Tags);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A row the reader refuses (it has no problem), standing at an index of a set.</summary>
    /// <param name="id">Its id.</param>
    /// <returns>The line.</returns>
    public static string Refused(string id)
    {
        return $$"""{"id":"{{id}}"}""";
    }

    /// <summary>A set of lines.</summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The set's text, each line ended by a line feed.</returns>
    public static string Of(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
            text.Append(line).Append('\n');

        return text.ToString();
    }

    /// <summary>
    /// The golden problems of the solver's tests (<see cref="SmtSolverCases"/>) whose options are
    /// one group's, as a set that puts each at its place in the list: every other place holds a
    /// refused row, so the rows of the run have the indices, and so the digests, of the golden file.
    /// The problem whose hash was forged has no row of a set and is left out.
    /// </summary>
    /// <param name="problems">The group's problems, each with its index.</param>
    /// <returns>The set's text.</returns>
    public static string Golden(IReadOnlyList<SmtProblemData> problems)
    {
        var byIndex = problems.Where(problem => problem.Id != "refused-hash").ToDictionary(problem => problem.Index);
        var last = byIndex.Keys.Max();

        return Of(Enumerable.Range(0, last + 1).Select(index => byIndex.TryGetValue(index, out var problem) ? Line(problem) : Refused($"other-{index}")));
    }

    #endregion

    #region Tools

    private static string Query(SmtQuery query)
    {
        return query switch
        {
            SmtQuery.Enumerate => "enumerate",
            SmtQuery.Prove => "prove",
            _ => "check"
        };
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IEnumerable<string> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    #endregion
}
