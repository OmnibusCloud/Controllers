using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Activities
{
    [Activity("Grid.ForEach")]
    public class WitActivityGridForEach : WitActivityTransform
    {
        #region Constructors

        public WitActivityGridForEach()
        {
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            return Options == null 
                ? $"{IterationVariable} {Keyword} {Collection}" 
                : $"{IterationVariable} {Keyword} {Collection}, {Options}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityGridForEach activity)
                return false;

            return base.Is(activity, tolerance)
                   && IterationVariable.Check(activity.IterationVariable)
                   && Collection.Check(activity.Collection)
                   && Keyword.Check(activity.Keyword)
                   && Options.Check(activity.Options);
        }

        protected override WitActivityGridForEach InnerClone()
        {
            return new WitActivityGridForEach
            {
                IterationVariable = IterationVariable?.Clone() as IWitReference,
                Collection = Collection?.Clone() as IWitParameter,
                Keyword = Keyword?.Clone() as IWitCondition,
                Options = Options?.Clone() as IWitReference
            };
        }

        #endregion

        #region Properties

        public IWitReference? IterationVariable { get; init; }

        public IWitParameter? Collection { get; init; }

        public IWitCondition? Keyword { get; init; }
        
        public IWitReference? Options { get; init; }

        #endregion


    }
}
