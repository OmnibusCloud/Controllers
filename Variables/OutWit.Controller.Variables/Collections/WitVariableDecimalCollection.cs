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
    [Variable("DecimalCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableDecimalCollection : WitCollection<decimal>, IWitVariableFactory<WitVariableDecimalCollection>
    {
        #region Constructors

        public WitVariableDecimalCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDecimalCollection(string name, IReadOnlyList<decimal> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableDecimalCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDecimalCollection Clone()
        {
            return new WitVariableDecimalCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDecimalCollection Create(string name)
        {
            return new WitVariableDecimalCollection(name);
        }

        #endregion
    }
}
