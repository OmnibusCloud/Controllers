using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("MatrixSparse.FromRows")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixSparseFromRows : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (RowsCollection == null)
                return $"{RowsCount}, {ColumnsCount}";

            return $"{RowsCount}, {ColumnsCount}, {RowsCollection}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityMatrixSparseFromRows activity)
                return false;

            return base.Is(activity, tolerance)
                   && RowsCollection.Check(activity.RowsCollection)
                   && RowsCount.Check(activity.RowsCount)
                   && ColumnsCount.Check(activity.ColumnsCount);
        }

        protected override WitActivityMatrixSparseFromRows InnerClone()
        {
            return new WitActivityMatrixSparseFromRows
            {
                RowsCollection = RowsCollection?.Clone() as IWitParameter,
                RowsCount = RowsCount?.Clone() as IWitParameter,
                ColumnsCount = ColumnsCount?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? RowsCollection { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? RowsCount { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? ColumnsCount { get; init; }

        #endregion
    }
}
