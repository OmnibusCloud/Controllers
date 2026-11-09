using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Activities
{
    // Not [MemoryPackable], like the other Grid activities: Grid is host-only, so this activity
    // never crosses the host-node boundary.
    /// <summary>
    /// Builds the options of a Grid.ForEach call whose selected items run more than once, each
    /// time on a different machine: <c>ProcessingOptions:checked = Grid.Replicas(2, 0.05);</c>.
    /// </summary>
    [Activity("Grid.Replicas")]
    public class WitActivityGridReplicas : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            return Share == null
                ? $"{Copies}"
                : $"{Copies}, {Share}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityGridReplicas activity)
                return false;

            return base.Is(activity, tolerance)
                   && Copies.Check(activity.Copies)
                   && Share.Check(activity.Share);
        }

        protected override WitActivityGridReplicas InnerClone()
        {
            return new WitActivityGridReplicas
            {
                Copies = Copies?.Clone() as IWitParameter,
                Share = Share?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        /// <summary>How many times a selected item runs.</summary>
        public IWitParameter? Copies { get; init; }

        /// <summary>The share of the items that are selected, 0 to 1; every item when absent.</summary>
        public IWitParameter? Share { get; init; }

        #endregion
    }
}
