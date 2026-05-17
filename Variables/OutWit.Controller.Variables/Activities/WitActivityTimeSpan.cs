using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("TimeSpan")]
    [MemoryPackable]
    public sealed partial class WitActivityTimeSpan : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Value != null)
                return $"{Value}";
            if (Days == null)
                return $"{Hours}, {Minutes}, {Seconds}";
            if (Milliseconds == null)
                return $"{Days}, {Hours}, {Minutes}, {Seconds}";

            return $"{Days}, {Hours}, {Minutes}, {Seconds}, {Milliseconds}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityTimeSpan activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value)
                   && Days.Check(activity.Days)
                   && Hours.Check(activity.Hours)
                   && Minutes.Check(activity.Minutes)
                   && Seconds.Check(activity.Seconds)
                   && Milliseconds.Check(activity.Milliseconds);


        }

        protected override WitActivityTimeSpan InnerClone()
        {
            return new WitActivityTimeSpan
            {
                Value = Value?.Clone() as IWitParameter,
                Days = Days?.Clone() as IWitParameter,
                Hours = Hours?.Clone() as IWitParameter,
                Minutes = Minutes?.Clone() as IWitParameter,
                Seconds = Seconds?.Clone() as IWitParameter,
                Milliseconds = Milliseconds?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Value { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Days { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Hours { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Minutes { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Seconds { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Milliseconds { get; init; }

        #endregion


    }
}
