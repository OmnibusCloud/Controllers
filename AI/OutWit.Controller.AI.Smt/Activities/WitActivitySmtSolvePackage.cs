using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// The node's primitive: a node solves a package of problems. The activity is given a reference
/// (<c>SmtTaskPackage</c>); the node downloads the package's blob, solves its problems with a pool
/// of sessions of the pinned solver, writes a row of each to a blob and returns the reference to it
/// (<c>SmtResultPackage</c>). A function of its input: the same package gives the same rows on
/// every node, so a retry or a copy elsewhere is safe. A node starts every package of its share
/// together so that the blobs arrive ahead of the work; one package is solved at a time in a
/// process, since its sessions take the node's cores. The planner sends it to machines of
/// <see cref="SmtSandboxDegree.MIN_RAM_MB"/> at least: below that one session at the default
/// options does not fit in what a machine has available.
/// </summary>
[Activity("Smt.SolvePackage")]
[CanRunInParallelOnClient(true)]
[RequiresResources(MinRamMb = SmtSandboxDegree.MIN_RAM_MB)]
[MemoryPackable]
public sealed partial class WitActivitySmtSolvePackage : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The reference it is given.</returns>
    protected override string InnerString()
    {
        return $"{Package}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtSolvePackage activity)
            return false;

        return base.Is(activity, tolerance)
               && Package.Check(activity.Package);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtSolvePackage InnerClone()
    {
        return new WitActivitySmtSolvePackage
        {
            Package = Package?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Package' argument of the activity: an <c>SmtTaskPackage</c>.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Package { get; init; }

    #endregion
}
