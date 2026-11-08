using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

/// <summary>
/// Collection of result batches — output of the Grid.ForEach over ExecuteBatch, input to Verify.Collect.
/// </summary>
[Variable("VerifyResultBatchCollection")]
[MemoryPackable]
public sealed partial class WitVariableVerifyResultBatchCollection : WitCollection<VerifyResultBatchData?>, IWitVariableFactory<WitVariableVerifyResultBatchCollection>
{
    #region Constructors

    public WitVariableVerifyResultBatchCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyResultBatchCollection(string name, IReadOnlyList<VerifyResultBatchData?> value)
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
        if (modelBase is not WitVariableVerifyResultBatchCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableVerifyResultBatchCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (VerifyResultBatchData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableVerifyResultBatchCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableVerifyResultBatchCollection Create(string name)
    {
        return new WitVariableVerifyResultBatchCollection(name);
    }

    #endregion
}
