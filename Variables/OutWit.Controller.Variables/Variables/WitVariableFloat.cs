using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Float")]
    [MemoryPackable]
    public sealed partial class WitVariableFloat : WitVariable<float>, IWitVariableFactory<WitVariableFloat>
    {
        #region Constructors

        public WitVariableFloat(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableFloat(string name, float value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableFloat variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableFloat Clone()
        {
            return new WitVariableFloat(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableFloat Create(string name)
        {
            return new WitVariableFloat(name);
        }

        #endregion
    }
}
