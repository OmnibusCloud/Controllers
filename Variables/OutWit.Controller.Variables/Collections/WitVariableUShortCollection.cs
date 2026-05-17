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
    [Variable("UShortCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableUShortCollection : WitCollection<ushort>, IWitVariableFactory<WitVariableUShortCollection>
    {
        #region Constructors

        public WitVariableUShortCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableUShortCollection(string name, IReadOnlyList<ushort> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableUShortCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableUShortCollection Clone()
        {
            return new WitVariableUShortCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableUShortCollection Create(string name)
        {
            return new WitVariableUShortCollection(name);
        }

        #endregion
    }
}
