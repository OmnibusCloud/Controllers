using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("SByte")]
    [MemoryPackable]
    public sealed partial class WitVariableSByte : WitVariable<sbyte>, IWitVariableFactory<WitVariableSByte>
    {
        #region Constructors

        public WitVariableSByte(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableSByte(string name, sbyte value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableSByte variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableSByte Clone()
        {
            return new WitVariableSByte(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableSByte Create(string name)
        {
            return new WitVariableSByte(name);
        }

        #endregion
    }
}
