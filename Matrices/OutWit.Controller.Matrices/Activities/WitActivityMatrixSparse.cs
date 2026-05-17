using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("MatrixSparse")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixSparse : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Rows == null && Columns == null)
                return $"{Data}";

            if (Data == null)
                return $"{Rows}, {Columns}";

            return $"{Rows}, {Columns}, {Data}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityMatrixSparse activity)
                return false;

            return base.Is(activity, tolerance)
                   && Data.Check(activity.Data)
                   && Rows.Check(activity.Rows)
                   && Columns.Check(activity.Columns);
        }

        protected override WitActivityMatrixSparse InnerClone()
        {
            return new WitActivityMatrixSparse
            {
                Data = Data?.Clone() as IWitReference,
                Rows = Rows?.Clone() as IWitParameter,
                Columns = Columns?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? Data { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Rows { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Columns { get; init; }

        #endregion

        #endregion
    }
}
