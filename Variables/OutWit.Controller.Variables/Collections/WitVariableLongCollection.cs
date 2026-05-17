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
    [Variable("LongCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableLongCollection : WitCollection<long>, IWitVariableFactory<WitVariableLongCollection>
    {
        #region Constructors

        public WitVariableLongCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableLongCollection(string name, IReadOnlyList<long> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableLongCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableLongCollection Clone()
        {
            return new WitVariableLongCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableLongCollection Create(string name)
        {
            return new WitVariableLongCollection(name);
        }

        #endregion
    }
}
