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
    /// <summary>
    /// Collection variable containing references to blobs in WitCloud storage.
    /// Each element is a <see cref="Guid"/> blob identifier.
    /// </summary>
    [Variable("BlobCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableBlobCollection : WitCollection<Guid?>, IWitVariableFactory<WitVariableBlobCollection>
    {
        #region Constructors

        public WitVariableBlobCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableBlobCollection(string name, IReadOnlyList<Guid?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableBlobCollection variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableBlobCollection Clone()
        {
            return new WitVariableBlobCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableBlobCollection Create(string name)
        {
            return new WitVariableBlobCollection(name);
        }

        #endregion
    }
}
