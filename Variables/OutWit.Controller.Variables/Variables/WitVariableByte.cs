using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Byte")]
    [MemoryPackable]
    public sealed partial class WitVariableByte : WitVariable<byte>, IWitVariableFactory<WitVariableByte>
    {
        #region Constructors

        public WitVariableByte(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableByte(string name, byte value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableByte variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableByte Clone()
        {
            return new WitVariableByte(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableByte Create(string name)
        {
            return new WitVariableByte(name);
        }

        #endregion
    }
}
