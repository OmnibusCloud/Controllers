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
    [Variable("ULongCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableULongCollection : WitCollection<ulong>, IWitVariableFactory<WitVariableULongCollection>
    {
        #region Constructors

        public WitVariableULongCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableULongCollection(string name, IReadOnlyList<ulong> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableULongCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableULongCollection Clone()
        {
            return new WitVariableULongCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableULongCollection Create(string name)
        {
            return new WitVariableULongCollection(name);
        }

        #endregion
    }
}
