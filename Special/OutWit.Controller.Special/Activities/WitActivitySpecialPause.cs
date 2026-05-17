using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Pause")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialPause : WitActivityCommand
    {
        #region Constructors

        public WitActivitySpecialPause()
        {
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            return Message == null
                ? $"{Timeout}" 
                : $"{Timeout}, {Message}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialPause activity)
                return false;

            return base.Is(activity, tolerance)
                   && Message.Check(activity.Message)
                   && Timeout.Check(activity.Timeout);
        }

        public override WitActivitySpecialPause Clone()
        {
            return new WitActivitySpecialPause
            {
                Message = Message?.Clone() as IWitParameter,
                Timeout = Timeout?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Message { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Timeout { get; init; } 
        
        #endregion
    }
}
