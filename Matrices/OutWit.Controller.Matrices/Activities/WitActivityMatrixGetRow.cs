using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("Matrix.GetRow")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixGetRow : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Matrix == null)
                return $"";

            if (Index == null)
                return $"{Matrix}";

            return $"{Matrix}, {Index}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityMatrixGetRow activity)
                return false;

            return base.Is(activity, tolerance)
                   && Matrix.Check(activity.Matrix)
                   && Index.Check(activity.Index);
        }

        protected override WitActivityMatrixGetRow InnerClone()
        {
            return new WitActivityMatrixGetRow
            {
                Matrix = Matrix?.Clone() as IWitReference,
                Index = Index?.Clone() as IWitParameter,
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? Matrix { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Index { get; init; }

        #endregion
    }
}
