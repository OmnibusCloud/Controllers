using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("GustavsonMultiply")]
    [CanRunInParallelOnClient(true)]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixGustavsonMultiply : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            return $"{RowIndex}, {RowVector}, {Matrix}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityMatrixGustavsonMultiply activity)
                return false;

            return base.Is(activity, tolerance)
                   && Matrix.Check(activity.Matrix)
                   && RowIndex.Check(activity.RowIndex)
                   && RowVector.Check(activity.RowVector);
        }

        protected override WitActivityMatrixGustavsonMultiply InnerClone()
        {
            return new WitActivityMatrixGustavsonMultiply
            {
                Matrix = Matrix?.Clone() as IWitReference,
                RowIndex = RowIndex?.Clone() as IWitParameter,
                RowVector = RowVector?.Clone() as IWitParameter,
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? Matrix { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? RowIndex { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? RowVector { get; init; }

        #endregion
    }
}
