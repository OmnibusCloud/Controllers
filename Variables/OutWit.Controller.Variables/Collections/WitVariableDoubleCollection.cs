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
    [Variable("DoubleCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableDoubleCollection : WitCollection<double>, IWitVariableFactory<WitVariableDoubleCollection>
    {
        #region Constructors

        public WitVariableDoubleCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDoubleCollection(string name, IReadOnlyList<double> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableDoubleCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDoubleCollection Clone()
        {
            return new WitVariableDoubleCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDoubleCollection Create(string name)
        {
            return new WitVariableDoubleCollection(name);
        }

        #endregion
    }
}
