using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("Double")]
    [MemoryPackable]
    public sealed partial class WitActivityDouble : WitActivityFunction
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
            if (modelBase is not WitActivityDouble activity)
                return false;

            return base.Is(activity, tolerance) 
                   && Value.Check(activity.Value);
        }

        protected override WitActivityDouble InnerClone()
        {
            return new WitActivityDouble
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
