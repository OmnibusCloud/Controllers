using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Delay")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialDelayed: WitActivityComposite
    {
        #region Constructors

        public WitActivitySpecialDelayed() 
            : base(false)
        {
            
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            return $"{Delay}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialDelayed activity)
                return false;

            return base.Is(activity, tolerance)
                   && Delay.Check(activity.Delay);
        }

        protected override WitActivitySpecialDelayed InnerClone()
        {
            return new WitActivitySpecialDelayed
            {
                Delay = Delay?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Delay { get; init; } 
        
        #endregion

    }
}
