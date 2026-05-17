using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Timer")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialTimer : WitActivityComposite
    {
        #region Constructors

        public WitActivitySpecialTimer()
        {
            StagesCount = 1;
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            return Timeout == null
                ? $"{Interval}"
                : $"{Interval}, {Timeout}";
        }

        public override void AddActivity(IWitActivity activity)
        {
            m_activities.Add(activity);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialTimer activity)
                return false;

            return base.Is(activity, tolerance)
                   && Interval.Check(activity.Interval)
                   && Timeout.Check(activity.Timeout);
        }

        protected override WitActivitySpecialTimer InnerClone()
        {
            return new WitActivitySpecialTimer
            {
                Interval = Interval?.Clone() as IWitParameter,
                Timeout = Timeout?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Interval { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Timeout { get; init; }

        #endregion
    }
}
