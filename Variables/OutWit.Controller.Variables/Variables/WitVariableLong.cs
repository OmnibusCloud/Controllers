using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Long")]
    [MemoryPackable]
    public sealed partial class WitVariableLong : WitVariable<long>, IWitVariableFactory<WitVariableLong>
    {
        #region Constructors

        public WitVariableLong(string name)
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableLong(string name, long value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableLong variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableLong Clone()
        {
            return new WitVariableLong(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableLong Create(string name)
        {
            return new WitVariableLong(name);
        }

        #endregion
    }
}
