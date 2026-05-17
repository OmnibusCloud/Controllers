using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("IntCollection")]
    [MemoryPackable]
    public sealed partial class WitActivityIntegerCollection : WitActivityFunction
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
            if (modelBase is not WitActivityIntegerCollection activity)
                return false;

            return base.Is(activity, tolerance) 
                   && Value.Check(activity.Value);
        }

        protected override WitActivityIntegerCollection InnerClone()
        {
            return new WitActivityIntegerCollection
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
