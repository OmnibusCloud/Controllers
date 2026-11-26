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
    /// <param name="cancellation">The job's cancellation, looked at before every row.</param>
    /// <returns>The scan.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static SmtProblemSetScan Read(string path, SmtOptionsData options, SmtProblemSpool? spool, CancellationToken cancellation = default)
    {
        var characters = new List<long>();
        SmtProblemSetSummary summary;

        using (var stream = Open(path))
        {
            var reader = new SmtProblemSetReader(stream, options);
            while (reader.TryRead(out var row))
            {
                cancellation.ThrowIfCancellationRequested();
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

    /// <summary>
    /// Options a reader takes: the options as they are when they are in range, else each one that
    /// is out of range at its default (a preflight still reads the set and says what is wrong).
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>Options in range.</returns>
    public static SmtOptionsData Readable(SmtOptionsData options)
    {
        if (SmtOptionsRules.Check(options).Count == 0)
            return options;

        var readable = options.Clone();
        if (!Enum.IsDefined(readable.Query))
            readable.Query = SmtQuery.Check;

        if (readable.MaxModels is < 0 or > SmtOptionsRules.MAX_MODELS)
            readable.MaxModels = 0;

        if (readable.Seed < 0)
            readable.Seed = 0;

        if (readable.ResourceLimit is < 0 or > SmtOptionsRules.MAX_RESOURCE_LIMIT)
            readable.ResourceLimit = 0;

        if (readable.HardCapSeconds is < 0 or > SmtOptionsRules.MAX_HARD_CAP_SECONDS)
            readable.HardCapSeconds = 0;

        if (readable.MemoryMegabytes is < 0 or > SmtOptionsRules.MAX_MEMORY_MEGABYTES)
            readable.MemoryMegabytes = 0;

        if (!(readable.SamplingRate >= SmtOptionsRules.MIN_SAMPLING_RATE && readable.SamplingRate <= SmtOptionsRules.MAX_SAMPLING_RATE))
            readable.SamplingRate = SmtOptionsRules.DEFAULT_SAMPLING_RATE;

        return readable;
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
