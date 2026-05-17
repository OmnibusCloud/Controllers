using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Short")]
    [MemoryPackable]
    public sealed partial class WitVariableShort : WitVariable<short>, IWitVariableFactory<WitVariableShort>
    {
        #region Constructors

        public WitVariableShort(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableShort(string name, short value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableShort variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableShort Clone()
        {
            return new WitVariableShort(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableShort Create(string name)
        {
            return new WitVariableShort(name);
        }

        #endregion
    }
}
