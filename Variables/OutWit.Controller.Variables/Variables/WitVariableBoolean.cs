using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Bool")]
    [MemoryPackable]
    public sealed partial class WitVariableBoolean : WitVariable<bool>, IWitVariableFactory<WitVariableBoolean>
    {
        #region Constructors

        public WitVariableBoolean(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableBoolean(string name, bool value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableBoolean variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableBoolean Clone()
        {
            return new WitVariableBoolean(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableBoolean Create(string name)
        {
            return new WitVariableBoolean(name);
        }

        #endregion
    }
}
