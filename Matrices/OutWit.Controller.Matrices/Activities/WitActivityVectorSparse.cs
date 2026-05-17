using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("VectorSparse")]
    [MemoryPackable]
    public sealed partial class WitActivityVectorSparse : WitActivityFunction
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
            if (modelBase is not WitActivityVectorSparse activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value);
        }

        protected override WitActivityVectorSparse InnerClone()
        {
            return new WitActivityVectorSparse
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
