using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The fields of a row as a lab writes them, before anybody has looked at them: written as a line
/// of a problem set for the host's reader, or handed to a node's plan as they are.
/// </summary>
/// <param name="Id">The row's id.</param>
/// <param name="Query">The row's query.</param>
/// <param name="Smt2">The problem's text.</param>
/// <param name="Values">The terms whose values a check asks for, or null for none.</param>
/// <param name="Project">The constants an enumeration is projected on, or null for none.</param>
/// <param name="Goal">The goal of a proof, or null for none.</param>
/// <param name="MaxModels">The most models of an enumeration, or null for the run's.</param>
internal sealed record SmtRawRow(string Id, SmtQuery Query, string Smt2, IReadOnlyList<string>? Values = null, IReadOnlyList<string>? Project = null, string? Goal = null, int? MaxModels = null)
{
    #region Functions

    /// <summary>The row as a line of a problem set.</summary>
    /// <returns>The line, without its line feed.</returns>
    public string Line()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", Id);
            writer.WriteString("query", Query switch { SmtQuery.Enumerate => "enumerate", SmtQuery.Prove => "prove", _ => "check" });
            writer.WriteString("smt2", Smt2);
            WriteArray(writer, "values", Values);
            WriteArray(writer, "project", Project);
            if (Goal != null)
                writer.WriteString("goal", Goal);
            if (MaxModels is { } models)
                writer.WriteNumber("max_models", models);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The row as a node would be given it if nobody had read it first.</summary>
    /// <param name="hash">The hash the problem comes with.</param>
    /// <returns>The problem.</returns>
    public SmtProblemData Problem(string hash)
    {
        return new SmtProblemData
        {
            Id = Id,
            Query = Query,
            Text = Smt2,
            Hash = hash,
            Values = Values?.ToList() ?? [],
            Project = Project?.ToList() ?? [],
            Goal = Goal ?? "",
            MaxModels = MaxModels ?? 0
        };
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IReadOnlyList<string>? items)
    {
        if (items == null)
            return;

        writer.WriteStartArray(name);
        foreach (var item in items)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    #endregion
}
