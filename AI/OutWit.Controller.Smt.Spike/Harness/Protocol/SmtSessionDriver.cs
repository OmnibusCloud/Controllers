using System.Diagnostics;
using System.Text;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Protocol;

/// <summary>
/// The interactive session of the specification (§4.1) as a state machine: it is asked for the
/// next piece of input whenever the solver has answered everything sent so far, reads the
/// nonce-delimited answers out of the solver's output, and decides what to send next. The same
/// driver serves the wasm guest (asked from inside the guest's fd_read) and the native process
/// (asked when every pending answer has arrived).
/// <para>
/// Queries: <c>check</c> (verdict; on sat the model and the values of every declared constant;
/// on unknown the reason; the statistics) and <c>enumerate</c> (the loop of §4.3 over a
/// projection, up to K models, complete only after a final unsat).
/// </para>
/// </summary>
internal sealed class SmtSessionDriver
{
    #region Constants

    private const string PRELUDE = "(set-option :print-success false)\n(set-option :produce-models true)\n";

    #endregion

    #region Fields

    private readonly SmtProblem m_problem;
    private readonly long m_rlimit;
    private readonly IReadOnlyList<SExpr> m_projection;
    private readonly int m_maxModels;
    private readonly Stopwatch m_clock;
    private readonly List<string> m_blocks = [];
    private State m_state = State.Start;
    private int m_consumed;
    private int m_scan;
    private int m_pending;
    private int m_tailBlocks;
    private long m_checkStartedMs;

    #endregion

    #region Constructors

    /// <summary>Makes a driver for one problem.</summary>
    /// <param name="problem">The canonical problem.</param>
    /// <param name="nonce">The token that ends every answer block (hex, never in a problem).</param>
    /// <param name="rlimit">The resource limit per check.</param>
    /// <param name="clock">The session's clock (for the check's duration; display only).</param>
    /// <param name="projection">For <c>enumerate</c>: the constants to project on; null for <c>check</c>.</param>
    /// <param name="maxModels">For <c>enumerate</c>: K.</param>
    public SmtSessionDriver(SmtProblem problem, string nonce, long rlimit, Stopwatch clock, IReadOnlyList<SExpr>? projection = null, int maxModels = 2)
    {
        m_problem = problem;
        Nonce = nonce;
        m_rlimit = rlimit;
        m_clock = clock;
        m_projection = projection ?? [];
        m_maxModels = maxModels;
        Query = projection == null ? "check" : "enumerate";
    }

    #endregion

    #region Functions

    /// <summary>
    /// The next piece of input, given everything the solver has written to stdout so far.
    /// </summary>
    /// <param name="stdout">The solver's whole stdout so far.</param>
    /// <param name="fuelUsed">The guest's fuel used so far (0 for a native process).</param>
    /// <returns>The text to send, or null for the end of input.</returns>
    public string? Next(string stdout, long fuelUsed)
    {
        ReadBlocks(stdout);
        switch (m_state)
        {
            case State.Start:
                m_state = State.AwaitVerdict;
                m_checkStartedMs = m_clock.ElapsedMilliseconds;
                return PRELUDE + m_problem.Canonical + $"(set-option :rlimit {m_rlimit})\n" + Check();

            case State.AwaitVerdict:
                if (!Ready())
                    return Abandon("the solver asked for input before it answered the check");
                return OnVerdict(Take(), fuelUsed);

            case State.AwaitProjection:
                if (!Ready())
                    return Abandon("the solver asked for input before it answered get-value");
                return OnProjection(Take());

            case State.AwaitTail:
                if (!Ready())
                    return Abandon("the solver asked for input before it answered the closing queries");
                for (var i = 0; i < m_tailBlocks; i++)
                    Tail.Add(Take());
                ParseTail();
                m_state = State.Done;
                return "(exit)\n";

            default:
                return null;
        }
    }

    private string Check()
    {
        return "(check-sat)\n" + Echo();
    }

    private string Echo()
    {
        m_pending++;
        return $"(echo \"{Nonce}\")\n";
    }

    private string OnVerdict(string block, long fuelUsed)
    {
        Checks++;
        CheckMs += m_clock.ElapsedMilliseconds - m_checkStartedMs;
        if (Checks == 1)
            FuelAtFirstVerdict = fuelUsed;

        var verdict = "none";
        foreach (var line in Lines(block))
        {
            if (line is "sat" or "unsat" or "unknown")
                verdict = line;
            else if (line.StartsWith("(error", StringComparison.Ordinal))
                Errors.Add(line);
        }

        Verdicts.Add(verdict);
        if (Checks == 1)
            Verdict = verdict;

        if (Query == "enumerate" && verdict == "sat" && m_projection.Count > 0)
        {
            m_state = State.AwaitProjection;
            return $"(get-value ({string.Join(" ", m_projection)}))\n" + Echo();
        }

        if (Query == "enumerate")
        {
            Complete = verdict == "unsat";
            CompleteReason = verdict == "unsat" ? "unsat" : verdict;
        }

        return Closing(verdict);
    }

    private string OnProjection(string block)
    {
        var values = block.Trim();
        Models.Add(values);
        if (Models.Count >= m_maxModels)
        {
            Complete = false;
            CompleteReason = "limit";
            return Closing("sat");
        }

        // Block this model and look again (§4.3).
        var pairs = SmtReader.ReadAll(values).FirstOrDefault()?.Items ?? [];
        var equalities = pairs.Where(p => p.IsList && p.Items.Count == 2).Select(p => $"(= {p.Items[0]} {p.Items[1]})").ToList();
        var conjunction = equalities.Count == 1 ? equalities[0] : $"(and {string.Join(" ", equalities)})";
        m_state = State.AwaitVerdict;
        m_checkStartedMs = m_clock.ElapsedMilliseconds;
        return $"(assert (not {conjunction}))\n" + Check();
    }

    private string Closing(string verdict)
    {
        var builder = new StringBuilder();
        m_tailBlocks = 0;
        if (Query == "check" && verdict == "sat")
        {
            builder.Append("(get-model)\n").Append(Echo());
            m_tailBlocks++;
            if (m_problem.Constants.Count > 0)
            {
                builder.Append("(get-value (").Append(string.Join(" ", m_problem.Constants.Select(c => c.Symbol.ToString()))).Append("))\n").Append(Echo());
                m_tailBlocks++;
            }
        }

        if (verdict == "unknown")
        {
            builder.Append("(get-info :reason-unknown)\n").Append(Echo());
            m_tailBlocks++;
        }

        builder.Append("(get-info :all-statistics)\n").Append(Echo());
        m_tailBlocks++;
        m_state = State.AwaitTail;
        return builder.ToString();
    }

    private void ParseTail()
    {
        var index = 0;
        if (Query == "check" && Verdict == "sat")
        {
            Model = Tail[index++].Trim();
            if (m_problem.Constants.Count > 0)
                Values = Tail[index++].Trim();
        }

        if (Verdicts.LastOrDefault() == "unknown")
        {
            var reason = Tail[index++].Trim();
            ReasonUnknown = ReadInfo(reason, ":reason-unknown") ?? reason;
        }

        Statistics = Tail[index].Trim();
        RlimitCount = ReadCount(Statistics, ":rlimit-count");
    }

    private string? Abandon(string why)
    {
        ProtocolError ??= why;
        m_state = State.Done;
        return null;
    }

    private bool Ready()
    {
        return m_blocks.Count >= m_pending && m_pending > 0;
    }

    private string Take()
    {
        var block = m_blocks[0];
        m_blocks.RemoveAt(0);
        m_pending--;
        return block;
    }

    private void ReadBlocks(string stdout)
    {
        // A block ends at the line equal to the nonce; nothing a model prints can end one early.
        while (true)
        {
            var at = stdout.IndexOf(Nonce, m_scan, StringComparison.Ordinal);
            if (at < 0)
                return;

            var lineStart = at == 0 || stdout[at - 1] == '\n';
            var lineEnd = at + Nonce.Length;
            if (lineEnd >= stdout.Length)
                return; // the line may not be complete yet

            var endsLine = stdout[lineEnd] == '\n' || stdout[lineEnd] == '\r';
            if (!lineStart || !endsLine)
            {
                m_scan = at + 1; // a mention inside a block, not the block's end
                continue;
            }

            m_blocks.Add(stdout.Substring(m_consumed, at - m_consumed));
            var next = stdout.IndexOf('\n', lineEnd);
            m_consumed = next < 0 ? stdout.Length : next + 1;
            m_scan = m_consumed;
        }
    }

    private static IEnumerable<string> Lines(string block)
    {
        return block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
    }

    private static string? ReadInfo(string block, string key)
    {
        try
        {
            var form = SmtReader.ReadAll(block).FirstOrDefault();
            if (form is { IsList: true } && form.Items.Count >= 2 && form.Items[0].Text == key)
                return form.Items[1].Text;
        }
        catch (FormatException)
        {
            // an unreadable answer stays as it is
        }

        return null;
    }

    private static long? ReadCount(string block, string key)
    {
        var at = block.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
            return null;

        var rest = block[(at + key.Length)..].TrimStart();
        var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
        return long.TryParse(digits, out var value) ? value : null;
    }

    #endregion

    #region Properties

    /// <summary>The token that ends every answer block.</summary>
    public string Nonce { get; }

    /// <summary><c>check</c> or <c>enumerate</c>.</summary>
    public string Query { get; }

    /// <summary>The first check's verdict: sat, unsat, unknown, or none when no verdict came.</summary>
    public string Verdict { get; private set; } = "none";

    /// <summary>Every check's verdict, in order (one for <c>check</c>).</summary>
    public List<string> Verdicts { get; } = [];

    /// <summary>The number of check-sat commands answered.</summary>
    public int Checks { get; private set; }

    /// <summary>The reason after an unknown (:reason-unknown), or null.</summary>
    public string? ReasonUnknown { get; private set; }

    /// <summary>The get-model answer after a sat, or null.</summary>
    public string? Model { get; private set; }

    /// <summary>The get-value answer for every declared constant after a sat, or null.</summary>
    public string? Values { get; private set; }

    /// <summary>For <c>enumerate</c>: the projection's values of every model found, in order.</summary>
    public List<string> Models { get; } = [];

    /// <summary>For <c>enumerate</c>: whether the count is exact (the last check was unsat).</summary>
    public bool Complete { get; private set; }

    /// <summary>For <c>enumerate</c>: unsat, limit, unknown or none.</summary>
    public string? CompleteReason { get; private set; }

    /// <summary>The get-info :all-statistics answer.</summary>
    public string? Statistics { get; private set; }

    /// <summary>The resource counter after the last check (:rlimit-count), cumulative over the session.</summary>
    public long? RlimitCount { get; private set; }

    /// <summary>Lines <c>(error ...)</c> the solver wrote before a verdict.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>The closing answers, in the order asked.</summary>
    public List<string> Tail { get; } = [];

    /// <summary>The guest's fuel used when the first verdict was read.</summary>
    public long FuelAtFirstVerdict { get; private set; }

    /// <summary>Milliseconds the checks took (from sending check-sat to its answer; display only).</summary>
    public long CheckMs { get; private set; }

    /// <summary>Why the session stopped early, or null when it ran its course.</summary>
    public string? ProtocolError { get; private set; }

    /// <summary>The number of answer blocks sent for and not yet read.</summary>
    public int Pending => m_pending;

    /// <summary>Whether the driver has nothing more to send.</summary>
    public bool Done => m_state == State.Done;

    #endregion

    #region Nested Types

    private enum State
    {
        Start,
        AwaitVerdict,
        AwaitProjection,
        AwaitTail,
        Done
    }

    #endregion
}
