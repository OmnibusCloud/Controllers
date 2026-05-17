using System;
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
    [Variable("GuidCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableGuidCollection : WitCollection<Guid?>, IWitVariableFactory<WitVariableGuidCollection>
    {
        #region Constructors

        public WitVariableGuidCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableGuidCollection(string name, IReadOnlyList<Guid?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableGuidCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableGuidCollection Clone()
        {
            return new WitVariableGuidCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableGuidCollection Create(string name)
        {
            return new WitVariableGuidCollection(name);
        }

        #endregion
    }
}
