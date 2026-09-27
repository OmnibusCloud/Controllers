using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Utils;

/// <summary>
/// The human-readable identity of a sweep variant for document clients - the ParaView plugin's
/// variant picker: the study's parameters paired with the variant's
/// substitution values, "XMAX=300, T=250", or the case's name in an OpenFOAM case set. Empty
/// when nothing names the variant (a deck-set study, a case without a name); readers then fall
/// back to the variant number.
/// </summary>
public static class SweepVariantLabel
{
    #region Constants

    public const string SEPARATOR = ", ";

    #endregion

    #region Functions

    /// <summary>
    /// Builds the label of one variant of a plan: its case's name in a case set, its values
    /// otherwise.
    /// </summary>
    /// <param name="plan">The plan; null yields an empty label.</param>
    /// <param name="variantIndex">Source-table index of the variant.</param>
    /// <returns>The case's name, the "Name=Value" pairs of <see cref="Of(SweepOptionsData?, int)"/>, or empty.</returns>
    public static string Of(SweepPlanData? plan, int variantIndex)
    {
        if (plan?.OpenFOAMSet is { } caseSet)
            return caseSet.Cases.FirstOrDefault(candidate => candidate.VariantIndex == variantIndex)?.Name ?? string.Empty;

        return Of(plan?.Options, variantIndex);
    }

    /// <summary>
    /// Builds the label of one variant of a study.
    /// </summary>
    /// <param name="options">The study (parameters and variants); null yields an empty label.</param>
    /// <param name="variantIndex">Source-table index of the variant.</param>
    /// <returns>"Name=Value" pairs joined by ", "; a parameter without a display name shows its token; a value beyond the parameter list shows as "pN"; empty when nothing identifies the variant.</returns>
    public static string Of(SweepOptionsData? options, int variantIndex)
    {
        if (options == null)
            return string.Empty;

        var variant = options.Variants.FirstOrDefault(candidate => candidate.VariantIndex == variantIndex);
        if (variant == null || variant.Values.Count == 0)
            return string.Empty;

        var parts = new List<string>(variant.Values.Count);
        for (var index = 0; index < variant.Values.Count; index++)
        {
            var parameter = index < options.Parameters.Count ? options.Parameters[index] : null;
            var name = parameter?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = parameter?.Token;
            if (string.IsNullOrWhiteSpace(name))
                name = $"p{index + 1}";
            parts.Add($"{name}={variant.Values[index]}");
        }

        return string.Join(SEPARATOR, parts);
    }

    #endregion
}
