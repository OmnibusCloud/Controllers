namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// Everything that can be decided about a case from its data alone, before a
/// byte of it is downloaded: the base tree, the recipe, the response request.
/// The one entry the node, the Sweep host and the initiator call; what needs
/// the files' contents (token coverage, run-time code) or the materialised
/// case is checked where those exist.
/// </summary>
public static class FoamCaseRules
{
    #region Functions

    /// <summary>
    /// Validates a case.
    /// </summary>
    /// <param name="data">The case; null is a finding.</param>
    /// <param name="hasExecutable">Answers whether the kit carries an executable; null skips the kit checks (host, initiator).</param>
    /// <returns>Findings, one sentence each, in the order tree, recipe, responses; empty when the case may be materialised.</returns>
    public static IReadOnlyList<string> Validate(FoamCaseData? data, Func<string, bool>? hasExecutable = null)
    {
        if (data == null)
            return ["The task carries no case."];

        var findings = new List<string>();
        findings.AddRange(FoamCasePathRules.ValidateTree(data.BaseFiles));
        findings.AddRange(FoamRecipeRules.Validate(data.Recipe, hasExecutable));
        findings.AddRange(FoamResponseRules.Validate(data.Extraction, data.BaseFiles.Select(file => file.RelativePath).ToList()));
        return findings;
    }

    #endregion
}
