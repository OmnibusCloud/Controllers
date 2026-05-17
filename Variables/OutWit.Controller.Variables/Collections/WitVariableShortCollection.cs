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
    [Variable("ShortCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableShortCollection : WitCollection<short>, IWitVariableFactory<WitVariableShortCollection>
    {
        #region Constructors

        public WitVariableShortCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableShortCollection(string name, IReadOnlyList<short> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableShortCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableShortCollection Clone()
        {
            return new WitVariableShortCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableShortCollection Create(string name)
        {
            return new WitVariableShortCollection(name);
        }

        #endregion
    }
}
