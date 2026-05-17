using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Guid")]
    [MemoryPackable]
    public sealed partial class WitVariableGuid : WitVariable<Guid?>, IWitVariableFactory<WitVariableGuid>
    {
        #region Constructors

        public WitVariableGuid(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableGuid(string name, Guid? value)
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
            if (modelBase is not WitVariableGuid variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableGuid Clone()
        {
            return new WitVariableGuid(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableGuid Create(string name)
        {
            return new WitVariableGuid(name);
        }

        #endregion
    }
}
