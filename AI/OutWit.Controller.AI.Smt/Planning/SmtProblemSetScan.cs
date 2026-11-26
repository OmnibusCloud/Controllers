using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Planning;

/// <summary>
/// One pass over a problem set as a run reads it (<see cref="SmtProblemSetReader"/>, a row at a
/// time): its summary, the size of every taken problem (what the packages are cut by), the
/// SHA-256 of its bytes, and, for a split, every taken problem kept in a spool. What is held in
/// memory is a row and a number a problem, whatever the set's size.
/// </summary>
internal sealed class SmtProblemSetScan
{
    #region Constants

    private const int READ_BUFFER_BYTES = 128 * 1024;

    #endregion

    #region Constructors

    private SmtProblemSetScan(SmtProblemSetSummary summary, List<long> characters, string sha256)
    {
        Summary = summary;
        Characters = characters;
        Sha256 = sha256;
    }

    #endregion

    #region Functions

    /// <summary>Reads a set.</summary>
    /// <param name="path">The set's local file.</param>
    /// <param name="options">The run's options, in range (<see cref="SmtOptionsRules.Check"/>).</param>
    /// <param name="spool">Where the taken problems are kept, or null when only the counts are needed.</param>
    /// <returns>The scan.</returns>
    public static SmtProblemSetScan Read(string path, SmtOptionsData options, SmtProblemSpool? spool)
    {
        var characters = new List<long>();
        SmtProblemSetSummary summary;

        using (var stream = Open(path))
        {
            var reader = new SmtProblemSetReader(stream, options);
            while (reader.TryRead(out var row))
            {
                if (!row.IsAccepted)
                    continue;

                characters.Add(SmtPackagePlanner.Characters(row.Data));
                spool?.Append(row.Data);
            }

            summary = reader.Summary;
        }

        return new SmtProblemSetScan(summary, characters, SmtPackageCodec.HashFile(path));
    }

    /// <summary>Opens a set's local file to be read from its start to its end, once.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The stream; the caller disposes it.</returns>
    public static FileStream Open(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, READ_BUFFER_BYTES, FileOptions.SequentialScan);
    }

    #endregion

    #region Properties

    /// <summary>What was read, counted.</summary>
    public SmtProblemSetSummary Summary { get; }

    /// <summary>The characters of every taken problem, in the set's order.</summary>
    public IReadOnlyList<long> Characters { get; }

    /// <summary>The SHA-256 of the set's bytes, lower-case hex.</summary>
    public string Sha256 { get; }

    #endregion
}
