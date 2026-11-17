using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Variables;

/// <summary>The task packages of a round — output of Corpus.Split, Corpus.Repairs and Corpus.Disputes, input to Grid.ForEach. References only: its size does not grow with the manifest beyond the most packages a round has.</summary>
[Variable("CorpusTaskPackageCollection")]
[MemoryPackable]
public sealed partial class WitVariableCorpusTaskPackageCollection : WitCollection<CorpusTaskPackageData?>, IWitVariableFactory<WitVariableCorpusTaskPackageCollection>
{
    #region Constructors

    public WitVariableCorpusTaskPackageCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableCorpusTaskPackageCollection(string name, IReadOnlyList<CorpusTaskPackageData?> value)
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
        if (modelBase is not WitVariableCorpusTaskPackageCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableCorpusTaskPackageCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (CorpusTaskPackageData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableCorpusTaskPackageCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableCorpusTaskPackageCollection Create(string name)
    {
        return new WitVariableCorpusTaskPackageCollection(name);
    }

    #endregion
}
