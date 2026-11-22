using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Variables;

/// <summary>A reference to one task package — the item Grid.ForEach hands a node; the tasks themselves are a blob.</summary>
[Variable("CorpusTaskPackage")]
[MemoryPackable]
public sealed partial class WitVariableCorpusTaskPackage : WitVariable<CorpusTaskPackageData?>, IWitVariableFactory<WitVariableCorpusTaskPackage>
{
    #region Constructors

    public WitVariableCorpusTaskPackage(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableCorpusTaskPackage(string name, CorpusTaskPackageData? value)
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
        if (modelBase is not WitVariableCorpusTaskPackage variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableCorpusTaskPackage Clone()
    {
        return new WitVariableCorpusTaskPackage(Name, (CorpusTaskPackageData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableCorpusTaskPackage Create(string name)
    {
        return new WitVariableCorpusTaskPackage(name);
    }

    #endregion
}
