using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// What one session has gathered so far: written by the conversation (<see cref="SmtSession"/>)
/// answer by answer, and read when the run is over, to make the outcome
/// (<see cref="SmtSessionOutcomes"/>). It belongs to one run, on the thread the solver runs on.
/// </summary>
internal sealed class SmtSessionRecord
{
    #region Constructors

    /// <summary>
    /// Creates the record of one session.
    /// </summary>
    /// <param name="plan">The problem and what is asked of it.</param>
    /// <param name="envelope">The session's envelope.</param>
    public SmtSessionRecord(SmtSolvePlan plan, SmtEnvelope envelope)
    {
        Plan = plan;
        Envelope = envelope;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Notes an answer the session could not go on from: what the outcome will say of it.
    /// </summary>
    /// <param name="status">The outcome's status.</param>
    /// <param name="code">The fixed word of its kind, or empty.</param>
    /// <param name="message">The sentence that goes with it.</param>
    public void Fail(SmtSolveStatus status, string code, string message)
    {
        Failed = true;
        FailureStatus = status;
        FailureCode = code;
        FailureMessage = message;
    }

    #endregion

    #region Properties

    /// <summary>The problem and what is asked of it.</summary>
    public SmtSolvePlan Plan { get; }

    /// <summary>The session's envelope.</summary>
    public SmtEnvelope Envelope { get; }

    /// <summary>The session's transcript.</summary>
    public SmtTranscript Transcript { get; } = new();

    /// <summary>The models that are whole, each as the solver printed it.</summary>
    public List<string> Models { get; } = [];

    /// <summary>The values that go with them, or the values a check asked for, each as the solver printed them.</summary>
    public List<string> Values { get; } = [];

    /// <summary>The verdict of the last check the solver answered; none before the first.</summary>
    public SmtVerdict Verdict { get; set; }

    /// <summary>Why the last check has no verdict, once the solver has said so.</summary>
    public SmtUnknownReason Reason { get; set; }

    /// <summary>The solver's own word for it.</summary>
    public string ReasonText { get; set; } = "";

    /// <summary>The checks sent.</summary>
    public int Checks { get; set; }

    /// <summary>The solver's resource counter after the last check it answered.</summary>
    public long ResourceCount { get; set; }

    /// <summary>Whether an enumeration saw its last check answer <c>unsat</c>.</summary>
    public bool Complete { get; set; }

    /// <summary>Whether an enumeration stopped at its number of models.</summary>
    public bool LimitReached { get; set; }

    /// <summary>Whether the session found itself cancelled at one of the solver's reads.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Whether the session ended the solver's input itself: the conversation ran its course, or stopped at an answer.</summary>
    public bool Over { get; set; }

    /// <summary>How many characters of the solver's stdout the session has been shown.</summary>
    public long Seen { get; set; }

    /// <summary>Whether an answer was given that the session could not go on from.</summary>
    public bool Failed { get; private set; }

    /// <summary>The status of such an answer's outcome.</summary>
    public SmtSolveStatus FailureStatus { get; private set; }

    /// <summary>The fixed word of its kind.</summary>
    public string FailureCode { get; private set; } = "";

    /// <summary>The sentence that goes with it.</summary>
    public string FailureMessage { get; private set; } = "";

    #endregion
}
