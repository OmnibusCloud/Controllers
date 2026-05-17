using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("Matrix.ColumnCount")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixColumnCount : WitActivityFunction
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
            if (modelBase is not WitActivityMatrixColumnCount activity)
                return false;

            return base.Is(activity, tolerance)
                   && Matrix.Check(activity.Matrix);
        }

        protected override WitActivityMatrixColumnCount InnerClone()
        {
            return new WitActivityMatrixColumnCount
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
