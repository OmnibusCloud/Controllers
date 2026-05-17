using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Collections
{
    [Variable("UIntCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableUIntegerCollection : WitCollection<uint>, IWitVariableFactory<WitVariableUIntegerCollection>
    {
        #region Constructors

        public WitVariableUIntegerCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableUIntegerCollection(string name, IReadOnlyList<uint> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableUIntegerCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableUIntegerCollection Clone()
        {
            return new WitVariableUIntegerCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableUIntegerCollection Create(string name)
        {
            return new WitVariableUIntegerCollection(name);
        }

        #endregion
    }
}
