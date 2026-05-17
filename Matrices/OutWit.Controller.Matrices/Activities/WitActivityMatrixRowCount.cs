using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("Matrix.RowCount")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixRowCount : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Matrix == null)
                return $"";

            return $"{Matrix}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityMatrixRowCount activity)
                return false;

            return base.Is(activity, tolerance)
                   && Matrix.Check(activity.Matrix);
        }

        protected override WitActivityMatrixRowCount InnerClone()
        {
            return new WitActivityMatrixRowCount
            {
                Matrix = Matrix?.Clone() as IWitReference,
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? Matrix { get; init; }

        #endregion
    }
}
