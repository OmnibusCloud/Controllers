using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("Matrix.GetColumns")]
    [MemoryPackable]
    public sealed partial class WitActivityMatrixGetColumns : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Matrix == null)
                return $"";

            if (Indices == null)
                return $"{Matrix}";

            return $"{Matrix}, {Indices}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityMatrixGetColumns activity)
                return false;

            return base.Is(activity, tolerance)
                   && Matrix.Check(activity.Matrix)
                   && Indices.Check(activity.Indices);
        }

        protected override WitActivityMatrixGetColumns InnerClone()
        {
            return new WitActivityMatrixGetColumns
            {
                Matrix = Matrix?.Clone() as IWitReference,
                Indices = Indices?.Clone() as IWitParameter,
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? Matrix { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Indices { get; init; }

        #endregion
    }
}
