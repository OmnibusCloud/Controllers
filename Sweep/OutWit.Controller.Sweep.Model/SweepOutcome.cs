namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// The sweep's verdict on one variant - the same three words for every
/// solver family, so a results table, a count and a document client need no
/// family knowledge to read it.
/// </summary>
public enum SweepOutcome
{
    /// <summary>The run finished cleanly and its responses were read.</summary>
    Succeeded = 0,

    /// <summary>The run started and did not finish cleanly: a step exited nonzero, the solve diverged or crashed.</summary>
    Failed = 1,

    /// <summary>The node refused the variant before anything ran and said why (run-time code, a step outside the allow-list, a token without a value).</summary>
    Refused = 2
}
