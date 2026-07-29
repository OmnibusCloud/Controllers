using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Extracts the response set from a finished solve's result files, on the
/// node, right after ccx exits. This is the growth point of the controller:
/// the .frd block reader (dataset names, DISP/STRESS components, temperature
/// steps) and the .dat readers (reaction-force sums, eigenfrequencies,
/// buckling factors) land here behind this one entry point.
/// </summary>
public static class CcxResponseExtractor
{
    #region Functions

    /// <summary>
    /// Extracts the automatic response set of the analysis plus the requested
    /// probes. The current implementation returns an empty row — the format
    /// readers arrive with their golden fixtures; the contract (row travels
    /// inline with the result, files stay on the node) is already final.
    /// </summary>
    /// <param name="frdPath">Path of the .frd result file; null when the solve produced none.</param>
    /// <param name="datPath">Path of the .dat result file; null when the solve produced none.</param>
    /// <param name="request">Requested probes; null = automatic set only.</param>
    /// <returns>The extracted response row.</returns>
    public static CcxResponseRowData Extract(string? frdPath, string? datPath, CcxExtractionRequestData? request)
    {
        return new CcxResponseRowData();
    }

    #endregion
}
