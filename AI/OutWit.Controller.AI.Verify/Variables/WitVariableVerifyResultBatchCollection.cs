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

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyResultBatchCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableVerifyResultBatchCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (VerifyResultBatchData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableVerifyResultBatchCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyResultBatchCollection Create(string name)
    {
        return new WitVariableVerifyResultBatchCollection(name);
    }

    #endregion
}
