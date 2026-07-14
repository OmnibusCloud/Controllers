namespace OutWit.Controller.Simulation.Schwarz.Tests.Utils;

internal static class SimulationTestPaths
{
    #region Functions

    public static string? FindControllersPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", "Debug");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    public static string? FindSolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    public static string GetSchwarzSolveScriptPath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, "Simulation", "OutWit.Controller.Simulation.Schwarz", "Scripts", "SchwarzSolve.wit");
    }

    #endregion
}
