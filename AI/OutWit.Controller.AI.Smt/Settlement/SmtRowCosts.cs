using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The numbers of a node's row held to the envelope its session ran in, which the package's
/// options give the host as they gave it to the node (<see cref="SmtProfile.Envelope"/>):
/// <list type="bullet">
/// <item>the fuel is what the session used of its budget, never more than the hard cap's fuel;</item>
/// <item>the peak memory is the solver's linear memory, which the host's cap of it ends;</item>
/// <item>the checks are the ones a session of the query sends: one for a check and for a proof;
/// for an enumeration one before each model it looks for, so no more than its number of
/// models (at that number it stops without another check);</item>
/// <item>a model is the answer of a check that said <c>sat</c>, and a verdict of <c>sat</c> or
/// <c>unsat</c> the answer of a check: neither is there without one;</item>
/// <item>the resource counter is read after a check, and is zero before the first.</item>
/// </list>
/// The resource counter has no ceiling here. The solver holds it to the resource limit inside a
/// check, but it also moves outside one (while the problem is read, while a model is read back),
/// and what it adds there is the solver's own measure of its work, in units the envelope does
/// not state. The wall time is a measurement of the machine: it has a floor only.
/// </summary>
internal static class SmtRowCosts
{
    #region Constants

    private const int CHECKS_OF_ONE_ANSWER = 1;

    #endregion

    #region Functions

    /// <summary>Whether the numbers of a row are ones a session of its problem can reach in the package's envelope.</summary>
    /// <param name="row">The row, its lists there.</param>
    /// <param name="bound">What the row's problem allows.</param>
    /// <param name="envelope">The envelope of a session of the package.</param>
    /// <returns>True when they are.</returns>
    public static bool Hold(SmtResultData row, SmtTaskBound bound, SmtEnvelope envelope)
    {
        if (row.Checks < 0 || row.ResourceCount < 0 || row.Fuel < 0 || row.PeakMemoryBytes < 0 || row.WallMs < 0)
            return false;

        if (row.Fuel > envelope.Fuel || row.PeakMemoryBytes > envelope.HostMemoryBytes)
            return false;

        if (row.Checks > (bound.Query == SmtQuery.Enumerate ? bound.MaxModels : CHECKS_OF_ONE_ANSWER) || row.Models.Count > row.Checks)
            return false;

        return row.Checks > 0 || (row.ResourceCount == 0 && row.Verdict is not (SmtResultWords.SAT or SmtResultWords.UNSAT));
    }

    #endregion
}
