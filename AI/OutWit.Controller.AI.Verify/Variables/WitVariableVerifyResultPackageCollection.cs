using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

/// <summary>
/// Collection of result packages (references to blobs of result batches): output of the Grid.ForEach over Verify.ExecutePackage, input to the collecting activities.
/// </summary>
[Variable("VerifyResultPackageCollection")]
[MemoryPackable]
public sealed partial class WitVariableVerifyResultPackageCollection : WitCollection<VerifyResultPackageData?>, IWitVariableFactory<WitVariableVerifyResultPackageCollection>
{
    #region Constructors

    public WitVariableVerifyResultPackageCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyResultPackageCollection(string name, IReadOnlyList<VerifyResultPackageData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyResultPackageCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableVerifyResultPackageCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (VerifyResultPackageData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableVerifyResultPackageCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableVerifyResultPackageCollection Create(string name)
    {
        return new WitVariableVerifyResultPackageCollection(name);
    }

    #endregion
}
