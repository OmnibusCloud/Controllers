using System.Diagnostics.CodeAnalysis;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// Reads a problem set: UTF-8 JSON lines, one problem a line (the schema of a row is published
/// beside this package's documentation). The same code reads a set wherever one is read, so a
/// row is taken or refused the same everywhere.
/// <para>
/// A set is read a row at a time and may be far larger than memory: one line is held, never the
/// set, and what is remembered of the rows read is bounded (their ids as a few bytes each, and
/// the <see cref="Summary"/>). A row is a problem (<see cref="SmtProblemData"/>, sanitized, with
/// its terms in canonical text) or a refusal with a fixed code; a refusal never carries more of
/// a row than the name of a field or one token cut to 64 characters.
/// </para>
/// <para>
/// Nothing here throws for what the stream holds. An <see cref="IOException"/> of the stream is
/// the caller's.
/// </para>
/// </summary>
public sealed class SmtProblemSetReader
{
    #region Fields

    private readonly SmtProblemSetLines m_lines;
    private readonly SmtProblemSetRowBuilder m_rows;
    private readonly long m_maxSetBytes;
    private bool m_ended;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a reader of a problem set.
    /// </summary>
    /// <param name="stream">The set, at its start; the caller owns the stream.</param>
    /// <param name="options">The run's options: the query of a row that names none, and the number of models of an enumeration that names none.</param>
    /// <exception cref="ArgumentNullException">There is no stream, or there are no options.</exception>
    /// <exception cref="ArgumentException">An option is out of its range (<see cref="SmtOptionsRules.Check"/>): options are checked before a set is read with them.</exception>
    public SmtProblemSetReader(Stream stream, SmtOptionsData options)
        : this(stream, options, SmtProblemSetLimits.MAX_SET_BYTES)
    {
    }

    /// <summary>
    /// Creates a reader of a problem set held to a bound of bytes of the caller's own: the tests
    /// meet the set's bound without making gigabytes.
    /// </summary>
    /// <param name="stream">The set, at its start; the caller owns the stream.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="maxSetBytes">The most bytes of the set.</param>
    /// <exception cref="ArgumentNullException">There is no stream, or there are no options.</exception>
    /// <exception cref="ArgumentException">An option is out of its range.</exception>
    internal SmtProblemSetReader(Stream stream, SmtOptionsData options, long maxSetBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        var problems = SmtOptionsRules.Check(options);
        if (problems.Count > 0)
            throw new ArgumentException(problems[0], nameof(options));

        m_maxSetBytes = maxSetBytes;
        m_lines = new SmtProblemSetLines(stream, SmtProblemSetLimits.MAX_LINE_BYTES, maxSetBytes);
        m_rows = new SmtProblemSetRowBuilder(SmtOptionsRules.Resolve(options));
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the next row of the set.
    /// </summary>
    /// <param name="row">The row: a problem or a refusal. Null when the set has no more rows.</param>
    /// <returns>False when the set has ended, or when the row after the last a set may have has been refused.</returns>
    /// <exception cref="IOException">The stream could not be read.</exception>
    public bool TryRead([NotNullWhen(true)] out SmtProblemSetRow? row)
    {
        row = null;
        if (m_ended)
            return false;

        if (!m_lines.MoveNext())
        {
            m_ended = true;
            Summary.BytesRead = m_lines.BytesRead;
            return false;
        }

        // A row's line is an int of the documents. Only a set of more than two gigabytes of blank
        // lines has a line past the largest one: such a line is numbered as that one, never as a
        // number that wrapped.
        var line = (int)Math.Min(m_lines.Number, int.MaxValue);
        var index = Summary.Rows;

        if (m_lines.PastBound)
        {
            // Said once, at the line that would take the set past its bound; nothing of it or after it is read.
            m_ended = true;
            row = SmtProblemSetRowBuilder.Refused(SmtRefusals.OfRow(SmtRefusalCodes.SET_TOO_LARGE,
                $"a problem set has at most {m_maxSetBytes} bytes; this line and what follows it were not read"), line, index);
        }
        else if (index >= SmtProblemSetLimits.MAX_ROWS)
        {
            // Said once, by the first row that is too many; nothing after it is read.
            m_ended = true;
            row = SmtProblemSetRowBuilder.Refused(SmtRefusals.OfRow(SmtRefusalCodes.TOO_MANY_PROBLEMS,
                $"a problem set has at most {SmtProblemSetLimits.MAX_ROWS} rows; this row and what follows it were not read"), line, index);
        }
        else if (m_lines.TooLong)
        {
            row = SmtProblemSetRowBuilder.Refused(SmtRefusals.OfRow(SmtRefusalCodes.ROW_TOO_LONG,
                $"the line is longer than the {SmtProblemSetLimits.MAX_LINE_BYTES} bytes a line may have"), line, index);
        }
        else
        {
            row = m_rows.Build(SmtProblemSetRowParser.Parse(m_lines.Line), line, index);
        }

        Summary.Add(row);
        Summary.BytesRead = m_lines.BytesRead;
        return true;
    }

    #endregion

    #region Properties

    /// <summary>What has been read so far, counted; complete when <see cref="TryRead"/> has returned false.</summary>
    public SmtProblemSetSummary Summary { get; } = new();

    #endregion
}
