using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping the outcome of ParaView.Validate.
/// </summary>
[Variable("ParaViewValidationReport")]
[MemoryPackable]
public sealed partial class WitVariableParaViewValidationReport : WitVariable<ParaViewValidationReportData?>, IWitVariableFactory<WitVariableParaViewValidationReport>
{
    #region Constructors

    public WitVariableParaViewValidationReport(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableParaViewValidationReport(string name, ParaViewValidationReportData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewValidationReport variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableParaViewValidationReport Clone()
    {
        return new WitVariableParaViewValidationReport(Name, (ParaViewValidationReportData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableParaViewValidationReport Create(string name)
    {
        return new WitVariableParaViewValidationReport(name);
    }

    #endregion
}
