using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The golden problems (<see cref="SmtSolverCases"/>) as packages: a package has one set of
/// options, so the cases are grouped by their options, in the order of the cases, and each problem
/// carries its case's place in the list as its index. Every machine makes the same packages.
/// </summary>
internal static class SmtGoldenPackages
{
    #region Functions

    /// <summary>The problems of the golden cases, each with its index, grouped by their options.</summary>
    /// <returns>The groups, in the order of their first case.</returns>
    public static IReadOnlyList<(SmtOptionsData Options, List<SmtProblemData> Problems)> Groups()
    {
        var groups = new List<(SmtOptionsData Options, List<SmtProblemData> Problems)>();
        var cases = SmtSolverCases.All;

        for (var index = 0; index < cases.Count; index++)
        {
            var problem = Indexed(cases[index].Problem, index);
            var group = groups.FindIndex(one => one.Options.Is(cases[index].Options));
            if (group < 0)
                groups.Add((cases[index].Options.Clone(), [problem]));
            else
                groups[group].Problems.Add(problem);
        }

        return groups;
    }

    /// <summary>A problem with an index.</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="index">Its index.</param>
    /// <returns>A copy with the index.</returns>
    public static SmtProblemData Indexed(SmtProblemData problem, int index)
    {
        var copy = problem.Clone();
        copy.Index = index;
        return copy;
    }

    #endregion
}
