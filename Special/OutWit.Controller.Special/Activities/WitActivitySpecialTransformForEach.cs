using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Transform.ForEach")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialTransformForEach : WitActivityTransform
    {
        #region Constructors

        public WitActivitySpecialTransformForEach()
        {
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            return $"{IterationVariable} {Keyword} {Collection}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialTransformForEach activity)
                return false;

            return base.Is(activity, tolerance)
                   && IterationVariable.Check(activity.IterationVariable)
                   && Collection.Check(activity.Collection)
                   && Keyword.Check(activity.Keyword);
        }

        protected override WitActivitySpecialTransformForEach InnerClone()
        {
            return new WitActivitySpecialTransformForEach
            {
                IterationVariable = IterationVariable?.Clone() as IWitReference,
                Collection = Collection?.Clone() as IWitParameter,
                Keyword = Keyword?.Clone() as IWitCondition
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitReference? IterationVariable { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Collection { get; init; }

        [MemoryPackAllowSerialize]
        public IWitCondition? Keyword { get; init; }

        #endregion

    }
}
