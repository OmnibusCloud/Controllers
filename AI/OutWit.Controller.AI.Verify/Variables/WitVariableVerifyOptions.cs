using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyOptions")]
[MemoryPackable]
public sealed partial class WitVariableVerifyOptions : WitVariable<VerifyOptionsData?>, IWitVariableFactory<WitVariableVerifyOptions>
{
    #region Constructors

    public WitVariableVerifyOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyOptions(string name, VerifyOptionsData? value)
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
        if (modelBase is not WitVariableVerifyOptions variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableVerifyOptions Clone()
    {
        return new WitVariableVerifyOptions(Name, (VerifyOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableVerifyOptions Create(string name)
    {
        return new WitVariableVerifyOptions(name);
    }

    #endregion
}
