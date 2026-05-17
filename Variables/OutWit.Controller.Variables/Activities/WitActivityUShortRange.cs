using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("UShort.Range")]
    [MemoryPackable]
    public sealed partial class WitActivityUShortRange : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            return Step == null 
                ? $"{From}, {To}" 
                : $"{From}, {To}, {Step}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityUShortRange activity)
                return false;

            return base.Is(activity, tolerance)
                   && From.Check(activity.From)
                   && To.Check(activity.To)
                   && Step.Check(activity.Step);
        }

        protected override WitActivityUShortRange InnerClone()
        {
            return new WitActivityUShortRange
            {
                From = From?.Clone() as IWitParameter,
                To = To?.Clone() as IWitParameter,
                Step = Step?.Clone() as IWitParameter,
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? From { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? To { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Step { get; init; }

        #endregion
    }
}
