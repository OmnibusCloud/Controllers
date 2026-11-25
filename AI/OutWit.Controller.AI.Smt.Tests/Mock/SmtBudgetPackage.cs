using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>A package of a test of the node's budget of sessions: its reference, its file, the file of its rows, its problems.</summary>
/// <param name="Name">The package's name: the start of its problems' ids.</param>
/// <param name="Reference">Its reference.</param>
/// <param name="ProblemsPath">Its file of problems.</param>
/// <param name="ResultsPath">The file its rows are written to.</param>
/// <param name="Problems">Its problems, in order.</param>
internal sealed record SmtBudgetPackage(string Name, SmtTaskPackageData Reference, string ProblemsPath, string ResultsPath, IReadOnlyList<SmtProblemData> Problems)
{
    /// <summary>The id of the problem at an index of the package.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The id.</returns>
    public string Id(int index)
    {
        return Problems[index].Id!;
    }
}
