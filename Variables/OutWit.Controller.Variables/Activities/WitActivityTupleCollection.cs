using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("TupleCollection")]
    [MemoryPackable]
    public sealed partial class WitActivityTupleCollection : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            return $"{Value}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityTupleCollection activity)
                return false;

            return base.Is(activity, tolerance) 
                   && Value.Check(activity.Value);
        }

        protected override WitActivityTupleCollection InnerClone()
        {
            return new WitActivityTupleCollection
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
