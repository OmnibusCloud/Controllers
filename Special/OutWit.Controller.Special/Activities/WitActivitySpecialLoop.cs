using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Loop")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialLoop : WitActivityComposite
    {
        #region Constructors

        public WitActivitySpecialLoop()
        {

        }

        #endregion

        #region Functions
        
        internal void SetStagesCount(int stagesCount)
        {
            StagesCount = stagesCount;
        }

        protected override string InnerString()
        {
            if (IterationsCount == null)
                return string.Empty;

            return $"{IterationsCount}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialLoop activity)
                return false;

            return base.Is(activity, tolerance)
                   && IterationsCount.Check(activity.IterationsCount);
        }

        protected override WitActivitySpecialLoop InnerClone()
        {
            return new WitActivitySpecialLoop
            {
                IterationsCount = IterationsCount?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? IterationsCount { get; init; } 
        
        #endregion
    }
}
