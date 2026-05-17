using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("TimeSpan")]
    [MemoryPackable]
    public sealed partial class WitVariableTimeSpan : WitVariable<TimeSpan?>, IWitVariableFactory<WitVariableTimeSpan>
    {
        #region Constructors

        public WitVariableTimeSpan(string name)
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableTimeSpan(string name, TimeSpan? value)
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
            if (modelBase is not WitVariableTimeSpan variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableTimeSpan Clone()
        {
            return new WitVariableTimeSpan(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableTimeSpan Create(string name)
        {
            return new WitVariableTimeSpan(name);
        }

        #endregion
    }
}
