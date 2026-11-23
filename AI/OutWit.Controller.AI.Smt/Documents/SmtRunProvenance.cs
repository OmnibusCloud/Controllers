using System.Reflection;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// What a run ran with, as its documents state it: the options as resolved (the numbers, never
/// "default"), the budget of a problem, the sampling rate the script stated, and the problem set
/// by its SHA-256 and size. The solver's pin, the engine's and the versions are the controller's
/// own and are read where they are written.
/// </summary>
/// <param name="Options">The run's options, resolved.</param>
/// <param name="SamplingRate">The sampling rate the script stated, or null.</param>
/// <param name="SetSha256">The SHA-256 of the problem set's bytes.</param>
/// <param name="SetBytes">The problem set's size in bytes.</param>
internal sealed record SmtRunProvenance(SmtOptionsData Options, double? SamplingRate, string SetSha256, long SetBytes)
{
    #region Functions

    /// <summary>Writes the options as resolved, with the budget of a problem as the numbers a node runs it with.</summary>
    /// <param name="writer">The document's writer, inside an object.</param>
    public void WriteOptions(Utf8JsonWriter writer)
    {
        var envelope = SmtProfile.Envelope(Options);

        writer.WriteStartObject("options");
        writer.WriteString("profile", SmtProfile.ID);
        writer.WriteString("query", SmtResultsWriter.Query(Options.Query));
        writer.WriteNumber("max_models", envelope.MaxModels);
        writer.WriteNumber("seed", envelope.Seed);
        writer.WriteNumber("rlimit", envelope.ResourceLimit);
        writer.WriteNumber("hard_cap_s", Options.HardCapSeconds);
        writer.WriteNumber("fuel", envelope.Fuel);
        writer.WriteNumber("memory_mb", envelope.MemoryMegabytes);
        writer.WriteNumber("sampling_rate", Options.SamplingRate);
        writer.WriteEndObject();
    }

    #endregion

    #region Properties

    /// <summary>The version of the model package this controller was built with.</summary>
    public static string ModelVersion
    {
        get
        {
            var informational = typeof(SmtProblemData).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                ?? typeof(SmtProblemData).Assembly.GetName().Version?.ToString(3)
                                ?? "";
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }
    }

    #endregion
}
