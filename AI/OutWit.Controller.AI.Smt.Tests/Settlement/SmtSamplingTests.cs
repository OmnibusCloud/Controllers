using System.Reflection;
using System.Runtime.Loader;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The number of packages a rate asks to be solved twice, as the host counts it, against Grid's
/// own rule: the planner of <c>Grid.Replicas</c>, from the module this build staged, plans the
/// executions of a call without a fan-out, and the executions past one a package are the packages
/// it selected. Over a table of sizes and rates the two say one number.
/// </summary>
[TestFixture]
public sealed class SmtSamplingTests
{
    #region Constants

    private const string PLANNER_ASSEMBLY = "OutWit.Controller.Grid.Model.dll";
    private const string PLANNER_TYPE = "OutWit.Controller.Grid.Model.WitGridReplicaPlanner";
    private const string PLANNER_METHOD = "Plan";

    private static readonly int[] SIZES = [1, 2, 3, 7, 10, 16, 19, 20, 33, 50, 99, 100, 101, 1_000, 1_024];

    private static readonly double[] RATES = [0, 1E-12, 1E-9, 0.001, 0.01, 0.05, 0.1, 0.15, 0.2, 0.25, 1.0 / 3, 0.3, 0.5, 0.7, 0.9, 0.95, 0.999, 1 - 1E-12, 1];

    #endregion

    #region Sampling Tests

    [Test]
    public void TheHostCountsThePackagesGridSelectsTest()
    {
        var module = Path.Combine(SmtRigModules.StagedRoot() ?? "", SmtRigModules.GRID, PLANNER_ASSEMBLY);
        if (!File.Exists(module))
            SmtRigModules.Missing($"no staged {PLANNER_ASSEMBLY} at '{module}'");

        // Grid's assembly in a context of its own, let go of afterwards: the engine tests load
        // the module themselves.
        var context = new AssemblyLoadContext("smt-sampling-grid", isCollectible: true);
        try
        {
            var plan = context.LoadFromAssemblyPath(module).GetType(PLANNER_TYPE, throwOnError: true)!
                .GetMethod(PLANNER_METHOD, BindingFlags.Public | BindingFlags.Static, [typeof(int), typeof(int), typeof(double), typeof(int)])!;

            Assert.Multiple(() =>
            {
                foreach (var size in SIZES)
                {
                    foreach (var rate in RATES)
                    {
                        // Two copies on two machines, as SmtRun asks: every execution past one a package is a selected package.
                        var slots = (IReadOnlyList<int>?)plan.Invoke(null, [size, 2, rate, 2]);
                        var grid = slots == null ? 0 : slots.Count - size;
                        Assert.That(SmtRunRig.Selected(size, rate), Is.EqualTo(grid), $"{size} packages at the rate {rate:R}");
                    }
                }
            });
        }
        finally
        {
            context.Unload();
        }
    }

    #endregion
}
