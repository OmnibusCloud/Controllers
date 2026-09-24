using OutWit.Controller.Sweep.Interfaces;
using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Families;

/// <summary>
/// The solver families the host knows, by <see cref="SweepFamily"/>. A new
/// family is one entry here and one <see cref="ISweepFamily"/> beside the
/// others.
/// </summary>
internal static class SweepFamilies
{
    #region Constants

    private static readonly IReadOnlyDictionary<SweepFamily, ISweepFamily> FAMILIES = new Dictionary<SweepFamily, ISweepFamily>
    {
        [SweepFamily.CalculiX] = new SweepFamilyCalculiX(),
        [SweepFamily.OpenFOAM] = new SweepFamilyOpenFOAM()
    };

    #endregion

    #region Functions

    /// <summary>
    /// The family implementation.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <returns>Its implementation.</returns>
    /// <exception cref="InvalidOperationException">The host does not know the family.</exception>
    public static ISweepFamily For(SweepFamily family)
    {
        return FAMILIES.TryGetValue(family, out var implementation)
            ? implementation
            : throw new InvalidOperationException($"This Sweep host does not know the solver family {family}.");
    }

    /// <summary>
    /// The family a validated plan runs on.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <returns>Its family's implementation.</returns>
    /// <exception cref="InvalidOperationException">The plan carries no options or no single family block (a plan the planner would have refused).</exception>
    public static ISweepFamily Of(SweepPlanData plan)
    {
        var family = plan.Options?.Family
                     ?? throw new InvalidOperationException("The plan's study carries no single family block.");
        return For(family);
    }

    #endregion
}
