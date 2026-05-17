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
    [Variable("BoolCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableBooleanCollection : WitCollection<bool>, IWitVariableFactory<WitVariableBooleanCollection>
    {
        #region Constructors

        public WitVariableBooleanCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableBooleanCollection(string name, IReadOnlyList<bool> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableBooleanCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableBooleanCollection Clone()
        {
            return new WitVariableBooleanCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableBooleanCollection Create(string name)
        {
            return new WitVariableBooleanCollection(name);
        }

        #endregion
    }
}
