using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Double")]
    [MemoryPackable]
    public sealed partial class WitVariableDouble : WitVariable<double>, IWitVariableFactory<WitVariableDouble>
    {
        #region Constructors

        public WitVariableDouble(string name)
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDouble(string name, double value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableDouble variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDouble Clone()
        {
            return new WitVariableDouble(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDouble Create(string name)
        {
            return new WitVariableDouble(name);
        }

        #endregion
    }
}
