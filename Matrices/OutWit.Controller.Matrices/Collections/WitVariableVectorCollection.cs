using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;
using OutWit.Common.Collections;

namespace OutWit.Controller.Matrices.Collections
{
    [Variable("VectorCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableVectorCollection : WitCollection<WitVector<double>?>, IWitVariableFactory<WitVariableVectorCollection>
    {
        #region Constructors

        public WitVariableVectorCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableVectorCollection(string name, IReadOnlyList<WitVector<double>?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableVectorCollection variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableVectorCollection Clone()
        {
            return new WitVariableVectorCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableVectorCollection Create(string name)
        {
            return new WitVariableVectorCollection(name);
        }

        #endregion
    }
}
