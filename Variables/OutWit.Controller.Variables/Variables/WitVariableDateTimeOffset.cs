using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("DateTimeOffset")]
    [MemoryPackable]
    public sealed partial class WitVariableDateTimeOffset : WitVariable<DateTimeOffset?>, IWitVariableFactory<WitVariableDateTimeOffset>
    {
        #region Constructors

        public WitVariableDateTimeOffset(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDateTimeOffset(string name, DateTimeOffset? value)
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
            if (modelBase is not WitVariableDateTimeOffset variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDateTimeOffset Clone()
        {
            return new WitVariableDateTimeOffset(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDateTimeOffset Create(string name)
        {
            return new WitVariableDateTimeOffset(name);
        }

        #endregion
    }
}
