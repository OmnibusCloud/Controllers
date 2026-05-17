using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Int")]
    [MemoryPackable]
    public sealed partial class WitVariableInteger : WitVariable<int>, IWitVariableFactory<WitVariableInteger>
    {
        #region Constructors

        public WitVariableInteger(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableInteger(string name, int value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableInteger variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableInteger Clone()
        {
            return new WitVariableInteger(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableInteger Create(string name)
        {
            return new WitVariableInteger(name);
        }

        #endregion
    }
}
