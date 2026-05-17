using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Jobs;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("If")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialIf : WitActivityComposite
    {
        #region Constructors

        public WitActivitySpecialIf()
        {
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            if (Right == null)
                return $"{Left}";
            
            return $"{Left} {Condition} {Right}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialIf activity)
                return false;

            return base.Is(activity, tolerance)
                   && Left.Check(activity.Left)
                   && Right.Check(activity.Right)
                   && Condition.Check(activity.Condition);
        }

        protected override WitActivitySpecialIf InnerClone()
        {
            return new WitActivitySpecialIf
            {
                Left = Left?.Clone() as IWitParameter,
                Right = Right?.Clone() as IWitParameter,
                Condition = Condition?.Clone() as IWitCondition
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Left { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Right { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitCondition? Condition { get; init; }

        #endregion
    }
}
