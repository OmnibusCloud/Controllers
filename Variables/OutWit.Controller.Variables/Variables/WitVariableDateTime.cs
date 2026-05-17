using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("DateTime")]
    [MemoryPackable]
    public sealed partial class WitVariableDateTime : WitVariable<DateTime?>, IWitVariableFactory<WitVariableDateTime>
    {
        #region Constructors

        public WitVariableDateTime(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDateTime(string name, DateTime? value)
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
            if (modelBase is not WitVariableDateTime variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDateTime Clone()
        {
            return new WitVariableDateTime(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDateTime Create(string name)
        {
            return new WitVariableDateTime(name);
        }

        #endregion
    }
}
