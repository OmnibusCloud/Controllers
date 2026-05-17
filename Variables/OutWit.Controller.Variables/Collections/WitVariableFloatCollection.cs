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
    [Variable("FloatCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableFloatCollection : WitCollection<float>, IWitVariableFactory<WitVariableFloatCollection>
    {
        #region Constructors

        public WitVariableFloatCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableFloatCollection(string name, IReadOnlyList<float> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableFloatCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableFloatCollection Clone()
        {
            return new WitVariableFloatCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableFloatCollection Create(string name)
        {
            return new WitVariableFloatCollection(name);
        }

        #endregion
    }
}
