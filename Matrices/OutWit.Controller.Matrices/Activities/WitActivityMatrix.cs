using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("Matrix")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrix : WitActivityFunction
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
            if (modelBase is not WitActivityMatrix activity)
                return false;

            return base.Is(activity, tolerance)
                   && Data.Check(activity.Data)
                   && Rows.Check(activity.Rows)
                   && Columns.Check(activity.Columns);
        }

        protected override WitActivityMatrix InnerClone()
        {
            return new WitActivityMatrix
            {
                Data = Data?.Clone() as IWitParameter,
                Rows = Rows?.Clone() as IWitParameter,
                Columns = Columns?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Data { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Rows { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Columns { get; init; }

        #endregion
    }
}
