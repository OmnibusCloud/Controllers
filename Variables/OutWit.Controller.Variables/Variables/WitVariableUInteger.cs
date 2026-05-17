using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("UInt")]
    [MemoryPackable]
    public sealed partial class WitVariableUInteger : WitVariable<uint>, IWitVariableFactory<WitVariableUInteger>
    {
        #region Constructors

        public WitVariableUInteger(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableUInteger(string name, uint value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableUInteger variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableUInteger Clone()
        {
            return new WitVariableUInteger(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableUInteger Create(string name)
        {
            return new WitVariableUInteger(name);
        }

        #endregion
    }
}
