using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Variables;

/// <summary>The tasks of a job — output of Corpus.Split, input to Grid.ForEach.</summary>
[Variable("CorpusTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariableCorpusTaskCollection : WitCollection<CorpusTaskData?>, IWitVariableFactory<WitVariableCorpusTaskCollection>
{
    #region Constructors

    public WitVariableCorpusTaskCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableCorpusTaskCollection(string name, IReadOnlyList<CorpusTaskData?> value)
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
        if (modelBase is not WitVariableCorpusTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableCorpusTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (CorpusTaskData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableCorpusTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableCorpusTaskCollection Create(string name)
    {
        return new WitVariableCorpusTaskCollection(name);
    }

    #endregion
}
