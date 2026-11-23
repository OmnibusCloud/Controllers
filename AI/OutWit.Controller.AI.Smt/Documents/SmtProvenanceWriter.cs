using System.Text.Json;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// Writes the provenance of a run (<c>docs/smt-provenance.schema.json</c>): the solver profile and
/// the SHA-256 of its module, the engine's version and the SHA-256 of its native library for each
/// platform the controller ships for, the controller's version and its model package's, the
/// options as resolved, and the problem set by its SHA-256. Every value is a pin or a number of
/// the run's own; nothing is said of the machines that ran it.
/// </summary>
internal static class SmtProvenanceWriter
{
    #region Constants

    /// <summary>The schema's name and version.</summary>
    public const string SCHEMA = "smt-provenance/1";

    #endregion

    #region Functions

    /// <summary>Writes the document.</summary>
    /// <param name="stream">Where.</param>
    /// <param name="provenance">What the run ran with.</param>
    /// <param name="rows">The rows of the problem set.</param>
    public static void Write(Stream stream, SmtRunProvenance provenance, int rows)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);

        writer.WriteStartObject("profile");
        writer.WriteString("id", SmtProfile.ID);
        writer.WriteString("solver", "z3");
        writer.WriteString("solver_version", SmtProfile.SOLVER_VERSION);
        writer.WriteString("module", SmtModuleLocator.RelativePath);
        writer.WriteString("module_sha256", SmtProfile.MODULE_SHA256);
        writer.WriteNumber("module_size", SmtProfile.MODULE_SIZE);
        writer.WriteEndObject();

        writer.WriteStartObject("engine");
        writer.WriteString("name", "wasmtime");
        writer.WriteString("version", WasmHostNatives.VERSION);
        writer.WriteStartObject("natives_sha256");
        foreach (var (rid, sha256) in WasmHostNatives.PINNED.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteString(rid, sha256);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteString("controller", ControllerBuildInfo.VERSION);
        writer.WriteString("model", SmtRunProvenance.ModelVersion);

        provenance.WriteOptions(writer);
        if (provenance.SamplingRate is { } rate)
            writer.WriteNumber("sampling_rate", rate);
        else
            writer.WriteNull("sampling_rate");

        writer.WriteStartObject("problem_set");
        writer.WriteString("sha256", provenance.SetSha256);
        writer.WriteNumber("bytes", provenance.SetBytes);
        writer.WriteNumber("rows", rows);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    #endregion
}
