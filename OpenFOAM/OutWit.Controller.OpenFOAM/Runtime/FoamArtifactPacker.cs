using System.Globalization;
using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Zips the part of a finished case the artifact policy asks for. Always:
/// <c>system/</c>, <c>constant/</c> without the mesh and without the geometry
/// inputs (the surfaces snappyHexMesh reads are the user's own files, tens of
/// megabytes, and would travel back in every variant), and an empty
/// <c>case.foam</c> stub, so whatever travels opens in ParaView as a case. By
/// policy: the time directories (latest or all), <c>constant/polyMesh</c>,
/// the step logs, <c>postProcessing/</c>. Never the <c>processor*</c> trees:
/// the reconstructed case is what a person opens.
/// </summary>
public static class FoamArtifactPacker
{
    #region Constants

    /// <summary>The stub file ParaView's reader opens.</summary>
    public const string STUB = "case.foam";

    /// <summary>Subtrees of <c>constant/</c> that are inputs, never results: left out of every artifact.</summary>
    public static readonly IReadOnlyList<string> GEOMETRY_INPUTS = ["triSurface/", "extendedFeatureEdgeMesh/", "geometry/"];

    #endregion

    #region Functions

    /// <summary>
    /// Packs the case.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="policy">What to include.</param>
    /// <param name="zipPath">Where the archive goes.</param>
    /// <returns>The archive's size in bytes.</returns>
    public static long Pack(string caseDirectory, FoamArtifactPolicyData policy, string zipPath)
    {
        var root = Path.GetFullPath(caseDirectory);
        var entries = SelectFiles(root, policy).ToList();

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in entries)
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, relative, CompressionLevel.Fastest);
            }

            archive.CreateEntry(STUB);
        }

        return new FileInfo(zipPath).Length;
    }

    /// <summary>
    /// The files the policy selects, as absolute paths.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="policy">What to include.</param>
    /// <returns>Files in a stable order.</returns>
    public static IEnumerable<string> SelectFiles(string caseDirectory, FoamArtifactPolicyData policy)
    {
        var root = Path.GetFullPath(caseDirectory);
        var selected = new SortedSet<string>(StringComparer.Ordinal);

        AddTree(selected, Path.Combine(root, "system"));

        var constant = Path.Combine(root, "constant");
        if (Directory.Exists(constant))
        {
            foreach (var file in Directory.EnumerateFiles(constant, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(constant, file).Replace('\\', '/');
                var inMesh = relative.StartsWith("polyMesh/", StringComparison.Ordinal);
                var isGeometryInput = GEOMETRY_INPUTS.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal));
                if (isGeometryInput)
                    continue;
                if (!inMesh || policy.Mesh)
                    selected.Add(file);
            }
        }

        foreach (var directory in TimeDirectories(root, policy.Times))
            AddTree(selected, directory);

        if (policy.Logs)
        {
            foreach (var file in Directory.EnumerateFiles(root, "log.*"))
                selected.Add(file);
        }

        if (policy.PostProcessing)
            AddTree(selected, Path.Combine(root, "postProcessing"));

        return selected;
    }

    /// <summary>
    /// The time directories the policy selects: none, the latest (largest
    /// numeric name) or all numeric ones.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="times">The policy's choice.</param>
    /// <returns>Absolute directories, ascending by time.</returns>
    public static IReadOnlyList<string> TimeDirectories(string caseDirectory, FoamArtifactTimes times)
    {
        if (times == FoamArtifactTimes.None)
            return [];

        var numeric = new List<(string Directory, double Time)>();
        foreach (var directory in Directory.EnumerateDirectories(caseDirectory))
        {
            if (ParseTime(Path.GetFileName(directory)) is { } time)
                numeric.Add((directory, time));
        }

        numeric.Sort((left, right) => left.Time.CompareTo(right.Time));
        var ordered = numeric.Select(entry => entry.Directory).ToList();

        if (ordered.Count == 0)
            return [];

        return times == FoamArtifactTimes.All ? ordered : [ordered[^1]];
    }

    private static double? ParseTime(string name)
    {
        return double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static void AddTree(SortedSet<string> selected, string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            selected.Add(file);
    }

    #endregion
}
