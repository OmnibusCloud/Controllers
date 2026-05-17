using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("VectorCollection")]
    [MemoryPackable]
    public sealed partial class WitActivityVectorCollection : WitActivityFunction
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
            if (modelBase is not WitActivityVectorCollection activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value);
        }

        protected override WitActivityVectorCollection InnerClone()
        {
            return new WitActivityVectorCollection
            {
                Value = Value?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Value { get; init; }

        #endregion
    }
}
