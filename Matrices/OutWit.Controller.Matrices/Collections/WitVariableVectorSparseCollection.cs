using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;
using OutWit.Common.Collections;

namespace OutWit.Controller.Matrices.Collections
{
    [Variable("VectorSparseCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableVectorSparseCollection : WitCollection<WitVectorSparse<double>?>, IWitVariableFactory<WitVariableVectorSparseCollection>
    {
        #region Constructors

        public WitVariableVectorSparseCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableVectorSparseCollection(string name, IReadOnlyList<WitVectorSparse<double>?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableVectorSparseCollection variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableVectorSparseCollection Clone()
        {
            return new WitVariableVectorSparseCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableVectorSparseCollection Create(string name)
        {
            return new WitVariableVectorSparseCollection(name);
        }

        #endregion
    }
}
