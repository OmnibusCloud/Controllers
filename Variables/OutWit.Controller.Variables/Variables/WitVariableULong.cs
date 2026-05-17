using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("ULong")]
    [MemoryPackable]
    public sealed partial class WitVariableULong : WitVariable<ulong>, IWitVariableFactory<WitVariableULong>
    {
        #region Constructors

        public WitVariableULong(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableULong(string name, ulong value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableULong variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableULong Clone()
        {
            return new WitVariableULong(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableULong Create(string name)
        {
            return new WitVariableULong(name);
        }

        #endregion
    }
}
