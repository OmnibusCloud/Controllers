using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Variables.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Color")]
    [MemoryPackable]
    public sealed partial class WitVariableColor : WitVariable<WitColor?>, IWitVariableFactory<WitVariableColor>
    {
        #region Constructors

        public WitVariableColor(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableColor(string name, WitColor? value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString()
        {
            return $"\"{Value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableColor variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && Value.Check(variable.Value);
        }

        public override WitVariableColor Clone()
        {
            return new WitVariableColor(Name, GetValue()?.Clone());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableColor Create(string name)
        {
            return new WitVariableColor(name);
        }

        #endregion
    }
}
