namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The decomposition every parallel run uses: scotch, over the rank count
/// the controller decided. Written over the case's own decomposeParDict by
/// design (requirements FR-F12): the user's machine count is not the node's,
/// and one method on every platform is what the kit ships (no kahip, plan D-17).
/// </summary>
public static class FoamDecomposition
{
    #region Constants

    /// <summary>
    /// The most ranks a run gets when the task asks for "all cores". Beyond
    /// this the single-node shared-memory transport and the per-rank
    /// overhead of small cases cost more than the cores give, and the kit's
    /// Open MPI is set to oversubscribe rather than refuse.
    /// </summary>
    public const int MAX_DEFAULT_RANKS = 16;

    /// <summary>The decomposition method (the kit builds scotch for it).</summary>
    public const string METHOD = "scotch";

    #endregion

    #region Functions

    /// <summary>
    /// The rank count for a task on this machine.
    /// </summary>
    /// <param name="requested">The task's Threads; 0 = all cores.</param>
    /// <returns>At least one, at most the cap when nothing was requested.</returns>
    public static int Ranks(int requested)
    {
        if (requested > 0)
            return requested;

        return Math.Clamp(Environment.ProcessorCount, 1, MAX_DEFAULT_RANKS);
    }

    /// <summary>
    /// Writes <c>system/decomposeParDict</c> for the given rank count.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="ranks">Number of subdomains.</param>
    /// <returns>The dictionary's path.</returns>
    public static string WriteDecomposeParDict(string caseDirectory, int ranks)
    {
        var path = Path.Combine(caseDirectory, "system", "decomposeParDict");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Dictionary(ranks));
        return path;
    }

    /// <summary>
    /// The dictionary text for a rank count.
    /// </summary>
    /// <param name="ranks">Number of subdomains.</param>
    /// <returns>A complete decomposeParDict.</returns>
    public static string Dictionary(int ranks)
    {
        return
            "FoamFile\n{\n    version     2.0;\n    format      ascii;\n    class       dictionary;\n    object      decomposeParDict;\n}\n\n" +
            $"numberOfSubdomains {ranks};\n\nmethod {METHOD};\n";
    }

    #endregion
}
