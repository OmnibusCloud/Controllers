using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("Array")]
    [MemoryPackable]
    public sealed partial class WitVariableArray : WitVariable<WitArray?>, IWitVariableFactory<WitVariableArray>
    {
        #region Constructors

        public WitVariableArray(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableArray(string name, WitArray? value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableArray variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && Value.Check(variable.Value);
        }

        public override WitVariableArray Clone()
        {
            return new WitVariableArray(Name, GetValue()?.Clone());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableArray Create(string name)
        {
            return new WitVariableArray(name);
        }

        #endregion
    }
}
