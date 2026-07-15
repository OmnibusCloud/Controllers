using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

/// <summary>
/// Collection of task batches — output of Verify.Split, input to Grid.ForEach.
/// </summary>
[Variable("VerifyTaskBatchCollection")]
[MemoryPackable]
public sealed partial class WitVariableVerifyTaskBatchCollection : WitCollection<VerifyTaskBatchData?>, IWitVariableFactory<WitVariableVerifyTaskBatchCollection>
{
    #region Constructors

    public WitVariableVerifyTaskBatchCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyTaskBatchCollection(string name, IReadOnlyList<VerifyTaskBatchData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyTaskBatchCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableVerifyTaskBatchCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (VerifyTaskBatchData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableVerifyTaskBatchCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyTaskBatchCollection Create(string name)
    {
        return new WitVariableVerifyTaskBatchCollection(name);
    }

    #endregion
}
