using System.IO.Compression;
using System.Text.RegularExpressions;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// The size of the mesh a run ended with, read from the note OpenFOAM writes
/// into the header of every <c>owner</c> it writes
/// (<c>"nPoints:218801  nCells:185237  ..."</c>) - a header in ASCII whatever
/// the format of the body, so a binary or a compressed mesh costs a few
/// lines. The mesh is the latest time's that carries one, else
/// <c>constant/polyMesh</c>'s; for a decomposed solve, the processor meshes
/// summed, because a case meshed on its decomposed form keeps the background
/// mesh in its root. Unknown (0) when a mesh is missing, carries no note, or
/// sits in the collated layout: the logs answer then.
/// </summary>
public static class FoamMeshReader
{
    #region Constants

    private const string CONSTANT = "constant";

    private const string POLY_MESH = "polyMesh";

    private static readonly string[] OWNER_FILES = ["owner", "owner.gz"];

    /// <summary>A header is a dozen lines; a file with no header this far has none.</summary>
    private const int MAX_HEADER_LINES = 40;

    private static readonly Regex NOTE_CELLS = new(@"nCells:\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PROCESSOR_NAME = new(@"^processor\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Reads the cell count of the mesh the run ended with.
    /// </summary>
    /// <param name="caseDirectory">The case.</param>
    /// <param name="decomposed">True when the solve ran on the processor directories.</param>
    /// <returns>The cell count; 0 when it cannot be read.</returns>
    public static long CellCount(string caseDirectory, bool decomposed)
    {
        if (!decomposed)
            return CellsOf(caseDirectory);

        var processors = Directory.EnumerateDirectories(caseDirectory)
            .Where(directory => PROCESSOR_NAME.IsMatch(Path.GetFileName(directory)))
            .ToList();
        if (processors.Count == 0)
            return 0;

        long total = 0;
        foreach (var processor in processors)
        {
            var cells = CellsOf(processor);
            if (cells == 0)
                return 0;

            total += cells;
        }

        return total;
    }

    #endregion

    #region Tools

    /// <summary>The cells of the latest mesh of one case (or processor) directory; 0 when unreadable.</summary>
    private static long CellsOf(string directory)
    {
        var candidates = FoamArtifactPacker.TimeDirectories(directory, FoamArtifactTimes.All)
            .Reverse()
            .Append(Path.Combine(directory, CONSTANT));

        foreach (var candidate in candidates)
        {
            var owner = OWNER_FILES.Select(file => Path.Combine(candidate, POLY_MESH, file)).FirstOrDefault(File.Exists);
            if (owner != null)
                return ReadNote(owner);
        }

        return 0;
    }

    private static long ReadNote(string owner)
    {
        using var file = File.OpenRead(owner);
        using var stream = owner.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file;
        using var reader = new StreamReader(stream);

        for (var index = 0; index < MAX_HEADER_LINES && reader.ReadLine() is { } line; index++)
        {
            var note = NOTE_CELLS.Match(line);
            if (note.Success)
                return long.TryParse(note.Groups[1].Value, out var cells) ? cells : 0;

            // The end of the FoamFile header: what follows is the body.
            if (line.TrimStart().StartsWith('}'))
                return 0;
        }

        return 0;
    }

    #endregion
}
