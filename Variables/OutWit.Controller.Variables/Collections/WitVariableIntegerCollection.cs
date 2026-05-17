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
    [Variable("IntCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableIntegerCollection : WitCollection<int>, IWitVariableFactory<WitVariableIntegerCollection>
    {
        #region Constructors

        public WitVariableIntegerCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableIntegerCollection(string name, IReadOnlyList<int> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableIntegerCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableIntegerCollection Clone()
        {
            return new WitVariableIntegerCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableIntegerCollection Create(string name)
        {
            return new WitVariableIntegerCollection(name);
        }

        #endregion
    }
}
