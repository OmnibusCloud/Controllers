using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// The verdict of a <c>checkMesh</c> log: OpenFOAM prints <c>Mesh OK.</c> or
/// <c>Failed N mesh checks.</c> at the end, and the cell count in its
/// mesh-stats block.
/// </summary>
public static class FoamCheckMeshReader
{
    #region Constants

    private static readonly Regex FAILED = new(@"Failed (\d+) mesh checks?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string OK = "Mesh OK.";

    /// <summary>The verdict text for a mesh that passed.</summary>
    public const string VERDICT_OK = "ok";

    #endregion

    #region Functions

    /// <summary>
    /// Reads a checkMesh log file.
    /// </summary>
    /// <param name="path">The log's path.</param>
    /// <returns>The verdict (<c>ok</c>, <c>failed N checks</c>) or null when the log is missing or says neither, and the cell count (0 when absent).</returns>
    public static (string? Verdict, long CellCount) Read(string path)
    {
        if (!File.Exists(path))
            return (null, 0);

        var text = File.ReadAllText(path);
        var facts = FoamLogReader.Read(new StringReader(text));

        if (text.Contains(OK, StringComparison.Ordinal))
            return (VERDICT_OK, facts.CellCount);

        var failed = FAILED.Match(text);
        if (failed.Success)
            return ($"failed {failed.Groups[1].Value} checks", facts.CellCount);

        return (null, facts.CellCount);
    }

    #endregion
}
