using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Planning;

/// <summary>
/// How a run's problems are cut into the packages Grid.ForEach places on nodes. A package travels
/// as one blob and is solved on one node, its problems on the node's sessions beside those of the
/// other packages of its share, so it should not be tiny (a transfer for a handful of problems)
/// and it must not be coarse (the split between nodes is only as fine as the packages, and a
/// package's result goes back only with its slowest problem). The rule: <see cref="MIN_PACKAGE_PROBLEMS"/> problems a package while the
/// run is small, and about <see cref="TARGET_PACKAGES"/> packages once it is large, never more than
/// a node takes (<see cref="SmtPackageCodec.MAX_PACKAGE_PROBLEMS"/>); a package also ends before
/// its problems' text passes <see cref="MAX_PACKAGE_CHARACTERS"/>. The cut depends on the problems
/// alone, never on the pool: one plan, one send. A repair round and a dispute round cut what they
/// send by the same rule.
/// </summary>
internal static class SmtPackagePlanner
{
    #region Constants

    /// <summary>The number of packages a large run is cut into: fine enough for a split by rate over hundreds of nodes.</summary>
    public const int TARGET_PACKAGES = 1024;

    /// <summary>The fewest problems a package holds while there are that many left: a transfer and a check carry enough work.</summary>
    public const int MIN_PACKAGE_PROBLEMS = 16;

    /// <summary>The most characters of text the problems of one package carry together (their texts and the terms beside them).</summary>
    public const long MAX_PACKAGE_CHARACTERS = 32L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>The most problems a package of a run holds.</summary>
    /// <param name="problems">The problems the run, or the round, sends.</param>
    /// <returns>From <see cref="MIN_PACKAGE_PROBLEMS"/> to <see cref="SmtPackageCodec.MAX_PACKAGE_PROBLEMS"/>.</returns>
    public static int ProblemsPerPackage(long problems)
    {
        return (int)Math.Clamp((Math.Max(0, problems) + TARGET_PACKAGES - 1) / TARGET_PACKAGES, MIN_PACKAGE_PROBLEMS, SmtPackageCodec.MAX_PACKAGE_PROBLEMS);
    }

    /// <summary>The characters a problem carries to a node: its text and the terms beside it.</summary>
    /// <param name="problem">The problem.</param>
    /// <returns>Characters.</returns>
    public static long Characters(SmtProblemData problem)
    {
        long characters = (problem.Text?.Length ?? 0) + (problem.Goal?.Length ?? 0) + (problem.Id?.Length ?? 0) + (problem.Tags?.Length ?? 0);
        foreach (var value in problem.Values ?? [])
            characters += value.Length;

        foreach (var name in problem.Project ?? [])
            characters += name.Length;

        return characters;
    }

    /// <summary>How many packages problems of given sizes are cut into, in their order.</summary>
    /// <param name="characters">Each problem's characters (<see cref="Characters"/>).</param>
    /// <returns>The packages.</returns>
    public static int Count(IReadOnlyList<long> characters)
    {
        var cutter = new SmtPackageCutter(ProblemsPerPackage(characters.Count));
        var packages = 0;
        foreach (var size in characters)
        {
            if (cutter.StartsNew(size))
            {
                packages++;
                cutter.Reset();
            }

            cutter.Add(size);
        }

        return cutter.Problems > 0 ? packages + 1 : packages;
    }

    #endregion
}
