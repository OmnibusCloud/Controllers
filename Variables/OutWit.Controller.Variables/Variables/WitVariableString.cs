using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("String")]
    [MemoryPackable]
    public sealed partial class WitVariableString : WitVariable<string?>, IWitVariableFactory<WitVariableString>
    {
        #region Constructors

        public WitVariableString(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableString(string name, string? value)
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
            if (modelBase is not WitVariableString variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableString Clone()
        {
            return new WitVariableString(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableString Create(string name)
        {
            return new WitVariableString(name);
        }

        #endregion
    }
}
