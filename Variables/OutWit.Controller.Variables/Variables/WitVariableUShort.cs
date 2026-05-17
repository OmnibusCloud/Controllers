using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("UShort")]
    [MemoryPackable]
    public sealed partial class WitVariableUShort : WitVariable<ushort>, IWitVariableFactory<WitVariableUShort>
    {
        #region Constructors

        public WitVariableUShort(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableUShort(string name, ushort value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableUShort variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableUShort Clone()
        {
            return new WitVariableUShort(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableUShort Create(string name)
        {
            return new WitVariableUShort(name);
        }

        #endregion
    }
}
