namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// What the host took of one fan-out's results (<see cref="SmtIntakeRig"/>), as a test looks at
/// it: numbers and words, nothing of the controller's own types.
/// </summary>
/// <param name="Accepted">The result packages taken.</param>
/// <param name="Rejected">The result packages refused, by reason.</param>
/// <param name="Faults">The packages a node said it could not work on, by the node's code.</param>
/// <param name="SpoolBytes">The bytes of rows the spool held when the fan-out was taken in.</param>
internal sealed record SmtIntake(int Accepted, IReadOnlyDictionary<string, int> Rejected, IReadOnlyDictionary<string, int> Faults, long SpoolBytes)
{
    #region Functions

    /// <summary>The result packages refused for a reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The number; 0 when none was.</returns>
    public int RejectedFor(string reason)
    {
        return Rejected.GetValueOrDefault(reason);
    }

    #endregion
}
