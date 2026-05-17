using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Decimal")]
    [MemoryPackable]
    public sealed partial class WitVariableDecimal : WitVariable<decimal>, IWitVariableFactory<WitVariableDecimal>
    {
        #region Constructors

        public WitVariableDecimal(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDecimal(string name, decimal value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableDecimal variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDecimal Clone()
        {
            return new WitVariableDecimal(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDecimal Create(string name)
        {
            return new WitVariableDecimal(name);
        }

        #endregion
    }
}
