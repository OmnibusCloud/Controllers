using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("Object")]
    [MemoryPackable]
    public sealed partial class WitActivityObject : WitActivityFunction
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
            if (modelBase is not WitActivityObject activity)
                return false;

            return base.Is(activity, tolerance) 
                   && Value.Check(activity.Value);
        }

        protected override WitActivityObject InnerClone()
        {
            return new WitActivityObject
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
