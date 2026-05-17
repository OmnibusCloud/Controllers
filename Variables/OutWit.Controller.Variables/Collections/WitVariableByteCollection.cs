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
    [Variable("ByteCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableByteCollection : WitCollection<byte>, IWitVariableFactory<WitVariableByteCollection>
    {
        #region Constructors

        public WitVariableByteCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableByteCollection(string name, IReadOnlyList<byte> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableByteCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableByteCollection Clone()
        {
            return new WitVariableByteCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableByteCollection Create(string name)
        {
            return new WitVariableByteCollection(name);
        }

        #endregion
    }
}
