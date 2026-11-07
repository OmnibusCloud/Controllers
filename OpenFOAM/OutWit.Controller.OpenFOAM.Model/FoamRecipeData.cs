using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// What runs on the node for every variant, in order: the recipe derived from
/// the case's <c>Allrun</c> or from its contents, edited within the allow-list
/// by the user. The application is named separately because it is the step
/// whose log carries the convergence facts.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamRecipeData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamRecipeData recipe)
            return false;

        return Application.Is(recipe.Application)
               && MeshesPerVariant.Is(recipe.MeshesPerVariant)
               && Steps.IsSequence(recipe.Steps, tolerance);
    }

    public override FoamRecipeData Clone()
    {
        return new FoamRecipeData
        {
            Application = Application,
            MeshesPerVariant = MeshesPerVariant,
            Steps = Steps.Select(step => step.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"{Application}: {Steps.Count} step(s){(MeshesPerVariant ? ", meshes per variant" : "")}";
    }

    #endregion

    #region Properties

    /// <summary>The solver (<c>simpleFoam</c>, <c>interFoam</c>): the step whose log is read for convergence.</summary>
    [MemoryPackOrder(0)]
    public string Application { get; set; } = string.Empty;

    /// <summary>True when a parameter changes the mesh, so every variant meshes again.</summary>
    [MemoryPackOrder(1)]
    public bool MeshesPerVariant { get; set; }

    /// <summary>The steps, in execution order.</summary>
    [MemoryPackOrder(2)]
    public List<FoamStepData> Steps { get; set; } = [];

    #endregion
}
