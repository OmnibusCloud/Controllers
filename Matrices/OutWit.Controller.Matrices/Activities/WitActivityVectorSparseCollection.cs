using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("VectorSparseCollection")]
    [MemoryPackable]
    public sealed partial class WitActivityVectorSparseCollection : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            return Value == null ? $"" : $"{Value}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityVectorSparseCollection activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value);
        }

        protected override WitActivityVectorSparseCollection InnerClone()
        {
            return new WitActivityVectorSparseCollection
            {
                Value = Value?.Clone() as IWitReference
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? Value { get; init; }

        #endregion
    }
}
