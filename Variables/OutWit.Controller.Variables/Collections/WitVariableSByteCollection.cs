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
    [Variable("SByteCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableSByteCollection : WitCollection<sbyte>, IWitVariableFactory<WitVariableSByteCollection>
    {
        #region Constructors

        public WitVariableSByteCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableSByteCollection(string name, IReadOnlyList<sbyte> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableSByteCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableSByteCollection Clone()
        {
            return new WitVariableSByteCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableSByteCollection Create(string name)
        {
            return new WitVariableSByteCollection(name);
        }

        #endregion
    }
}
