using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// One session with the solver: one problem, one query, one conversation, one outcome. The
/// session is the caller's side of the conversation (<see cref="Next"/>): it is asked each time
/// the solver has read everything it was sent, is shown what the solver wrote since, and gives
/// the next command. One command that is answered goes out at a time, so the text between two
/// asks is that command's answer, whole: no marker has to be looked for in it, and nothing a
/// model holds can end an answer early. When the run is over, <see cref="Finish"/> makes the
/// outcome of how it ended and of what was gathered (<see cref="SmtSessionOutcomes"/>).
/// <para>
/// What is sent, in order: the profile's options; the problem as the sanitizer gave it for the
/// solver; for a proof the negated goal; the resource limit of a check. These state something
/// and are answered by nothing: any answer to them is an error in what was given. Then
/// <c>(check-sat)</c>, and after every check the statistics, for the resource counter. A
/// <c>check</c> or a <c>prove</c> asks the model after <c>sat</c> (and a check the values of the
/// terms its row names), the reason after <c>unknown</c>. An <c>enumerate</c> asks, after each
/// <c>sat</c>, the values of the projected constants and the model, then asserts that not all
/// of those constants have those values and checks again, until it has its number of models, a
/// check says <c>unsat</c> (the count is exact) or <c>unknown</c>.
/// </para>
/// <para>
/// A session serves one run, on the thread the solver runs on, and decides nothing by a clock.
/// </para>
/// </summary>
internal sealed class SmtSession
{
    #region Constants

    private const string INFO_STATISTICS = ":all-statistics";
    private const string INFO_REASON = ":reason-unknown";

    // The solver's words for a check its resource limit stopped: the first when the limit made
    // it give up, the second when the limit ended it by an exception.
    private const string REASON_CANCELED = "canceled";
    private const string REASON_RESOURCE_LIMIT = "max. resource limit exceeded";
    private const string REASON_INCOMPLETE = "incomplete";
    private const string REASON_MEMORY = "memout";

    private static readonly UTF8Encoding TEXT = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    #endregion

    #region Fields

    private readonly SmtSolvePlan m_plan;
    private readonly SmtEnvelope m_envelope;
    private readonly CancellationToken m_cancellation;
    private readonly SmtSessionRecord m_record;
    private readonly Decoder m_decoder = TEXT.GetDecoder();

    private Step m_step = Step.Start;
    private long m_resourceBefore;

    // An enumeration's model in the making: its values are in, its model is still to come.
    private string? m_pendingValues;
    private IReadOnlyList<SmtExpression>? m_pendingTerms;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the session of one problem.
    /// </summary>
    /// <param name="plan">The problem and what is asked of it.</param>
    /// <param name="envelope">The session's envelope: the resource limit of a check.</param>
    /// <param name="cancellationToken">Looked at each time the solver asks for input: a cancelled session ends the input there.</param>
    /// <exception cref="ArgumentOutOfRangeException">The envelope's resource limit is not one the solver takes.</exception>
    public SmtSession(SmtSolvePlan plan, SmtEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.ResourceLimit < 1 || (ulong)envelope.ResourceLimit > SmtScripts.MAX_RESOURCE_LIMIT)
            throw new ArgumentOutOfRangeException(nameof(envelope), envelope.ResourceLimit, $"the resource limit of a check is between 1 and {SmtScripts.MAX_RESOURCE_LIMIT}");

        m_plan = plan;
        m_envelope = envelope;
        m_cancellation = cancellationToken;
        m_record = new SmtSessionRecord(plan, envelope);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The session's side of the conversation, as a run takes it: what the solver wrote since it
    /// was last asked in, its next input out.
    /// </summary>
    /// <param name="output">What the solver wrote to stdout since the previous call.</param>
    /// <returns>The next input as UTF-8; empty for the end of input.</returns>
    public ReadOnlyMemory<byte> Next(ReadOnlySpan<byte> output)
    {
        var answer = Decode(output);
        m_record.Transcript.Answer(answer);

        if (m_cancellation.IsCancellationRequested)
        {
            m_record.Cancelled = true;
            return End();
        }

        return m_step switch
        {
            Step.Start => OnStart(answer),
            Step.Setup => OnSetup(answer),
            Step.Check => OnVerdict(answer),
            Step.Statistics => OnStatistics(answer),
            Step.Reason => OnReason(answer),
            Step.Model => OnModel(answer),
            Step.Values => OnValues(answer),
            Step.Block => OnBlocked(answer),
            _ => End()
        };
    }

    /// <summary>
    /// Makes the outcome of a run that is over: how the run ended decides first, then what the
    /// conversation came to.
    /// </summary>
    /// <param name="run">The run's outcome.</param>
    /// <returns>The session's outcome.</returns>
    public SmtSolveOutcome Finish(WasmHostRunOutcome run)
    {
        return SmtSessionOutcomes.Of(m_record, run);
    }

    #endregion

    #region Conversation

    private ReadOnlyMemory<byte> OnStart(string answer)
    {
        if (!SmtAnswers.IsBlank(answer))
            return Unreadable("the solver wrote before it read its first command");

        var before = SmtProfile.Options(m_envelope.Seed);
        var after = new List<SmtExpression>();
        if (m_plan.Goal != null)
            after.Add(SmtScripts.AssertNot(m_plan.Goal));

        after.Add(SmtScripts.SetResourceLimit((ulong)m_envelope.ResourceLimit));

        foreach (var command in before)
            m_record.Transcript.Sent(command);

        m_record.Transcript.Note($"the problem: {m_plan.Problem.SolverCommands.Count} commands, SHA-256 {m_plan.Problem.Hash}");
        foreach (var command in after)
            m_record.Transcript.Sent(command);

        m_step = Step.Setup;
        return TEXT.GetBytes(SmtScripts.Text(before) + m_plan.Problem.SolverText + SmtScripts.Text(after));
    }

    private ReadOnlyMemory<byte> OnSetup(string answer)
    {
        // Options, declarations, definitions and assertions are answered by nothing.
        return SmtAnswers.IsBlank(answer) ? Check() : Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));
    }

    private ReadOnlyMemory<byte> OnVerdict(string answer)
    {
        if (SmtAnswers.IsError(answer))
            return Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));

        m_record.Verdict = SmtAnswers.Verdict(answer);
        if (m_record.Verdict == SmtVerdict.None)
            return Unreadable("the answer of check-sat is none of sat, unsat and unknown");

        m_step = Step.Statistics;
        return Send(SmtScripts.GetInfo(INFO_STATISTICS));
    }

    private ReadOnlyMemory<byte> OnStatistics(string answer)
    {
        if (SmtAnswers.IsError(answer))
            return Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));

        if (SmtAnswers.ResourceCount(answer) is not { } count)
            return Unreadable("the answer of get-info :all-statistics is not a list of statistics");

        m_record.ResourceCount = count;

        switch (m_record.Verdict)
        {
            case SmtVerdict.Sat when m_plan.Query == SmtQuery.Enumerate:
                m_step = Step.Values;
                return Send(SmtScripts.GetValue(m_plan.Projection));
            case SmtVerdict.Sat:
                m_step = Step.Model;
                return Send(SmtScripts.GetModel());
            case SmtVerdict.Unknown:
                m_step = Step.Reason;
                return Send(SmtScripts.GetInfo(INFO_REASON));
            default:
                // unsat: nothing more to ask; an enumeration has seen every model there is.
                m_record.Complete = m_plan.Query == SmtQuery.Enumerate;
                return End();
        }
    }

    private ReadOnlyMemory<byte> OnReason(string answer)
    {
        if (SmtAnswers.IsError(answer))
            return Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));

        if (SmtAnswers.IsBlank(answer))
            return Unreadable("the answer of get-info :reason-unknown is empty");

        m_record.ReasonText = SmtAnswers.Reason(answer);
        m_record.Reason = ReasonOf(m_record.ReasonText);
        return End();
    }

    private ReadOnlyMemory<byte> OnModel(string answer)
    {
        if (SmtAnswers.IsError(answer))
            return Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));

        if (SmtAnswers.IsBlank(answer))
            return Unreadable("the answer of get-model is empty");

        if (m_plan.Query != SmtQuery.Enumerate)
        {
            m_record.Models.Add(answer);
            if (m_plan.Values.Count == 0)
                return End();

            m_step = Step.Values;
            return Send(SmtScripts.GetValue(m_plan.Values));
        }

        // The model of an enumeration is whole now: its values came with the step before.
        m_record.Values.Add(m_pendingValues!);
        m_record.Models.Add(answer);
        var found = m_pendingTerms!;
        m_pendingValues = null;
        m_pendingTerms = null;

        if (m_record.Models.Count >= m_plan.MaxModels)
        {
            m_record.LimitReached = true;
            return End();
        }

        m_step = Step.Block;
        return Send(SmtScripts.Block(m_plan.Projection, found));
    }

    private ReadOnlyMemory<byte> OnValues(string answer)
    {
        if (SmtAnswers.IsError(answer))
            return Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));

        if (SmtAnswers.IsBlank(answer))
            return Unreadable("the answer of get-value is empty");

        if (m_plan.Query != SmtQuery.Enumerate)
        {
            m_record.Values.Add(answer);
            return End();
        }

        // The values are needed as terms: the next check is told that not all of them hold.
        if (SmtAnswers.Values(answer, m_plan.Projection) is not { } values)
            return Unreadable("the answer of get-value is not the values of the projected constants");

        m_pendingValues = answer;
        m_pendingTerms = values;
        m_step = Step.Model;
        return Send(SmtScripts.GetModel());
    }

    private ReadOnlyMemory<byte> OnBlocked(string answer)
    {
        // The assertion that rules the found model out is answered by nothing.
        return SmtAnswers.IsBlank(answer) ? Check() : Fail(SmtSolveStatus.InputError, "", SmtAnswers.Message(answer));
    }

    private ReadOnlyMemory<byte> Check()
    {
        m_record.Checks++;
        m_resourceBefore = m_record.ResourceCount;
        m_step = Step.Check;
        return Send(SmtScripts.CheckSat());
    }

    private ReadOnlyMemory<byte> Send(SmtExpression command)
    {
        m_record.Transcript.Sent(command);
        return TEXT.GetBytes(SmtScripts.Text([command]));
    }

    private ReadOnlyMemory<byte> Unreadable(string what)
    {
        return Fail(SmtSolveStatus.EngineCrash, SmtSolveCodes.UNREADABLE_ANSWER, what);
    }

    private ReadOnlyMemory<byte> Fail(SmtSolveStatus status, string code, string message)
    {
        m_record.Fail(status, code, message);
        return End();
    }

    private ReadOnlyMemory<byte> End()
    {
        m_record.Over = true;
        m_step = Step.Over;
        return ReadOnlyMemory<byte>.Empty;
    }

    private string Decode(ReadOnlySpan<byte> output)
    {
        if (output.IsEmpty)
            return "";

        var characters = new char[m_decoder.GetCharCount(output, flush: false)];
        var written = m_decoder.GetChars(output, characters, flush: false);
        m_record.Seen += written;
        return new string(characters, 0, written);
    }

    /// <summary>
    /// What an <c>unknown</c> is, by the solver's word and its counter: the resource limit only
    /// when the check spent the whole of it (the solver leaves its counter at the limit then).
    /// </summary>
    private SmtUnknownReason ReasonOf(string word)
    {
        if (word is REASON_CANCELED or REASON_RESOURCE_LIMIT && m_record.ResourceCount - m_resourceBefore >= m_envelope.ResourceLimit)
            return SmtUnknownReason.ResourceLimit;

        if (word.Contains(REASON_INCOMPLETE, StringComparison.Ordinal))
            return SmtUnknownReason.Incomplete;

        return string.Equals(word, REASON_MEMORY, StringComparison.Ordinal) ? SmtUnknownReason.Memory : SmtUnknownReason.Other;
    }

    #endregion

    #region Nested Types

    /// <summary>Where the conversation stands: the command whose answer comes next.</summary>
    private enum Step
    {
        Start,
        Setup,
        Check,
        Statistics,
        Reason,
        Model,
        Values,
        Block,
        Over
    }

    #endregion
}
