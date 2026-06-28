using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Activities
{
    // Intentionally not [MemoryPackable] / partial / sealed: Grid is a host-only
    // controller (the module class implements IWitControllerHost only), so the
    // Grid.Delegate activity never crosses the host↔node WitRPC boundary. What
    // crosses is the inner transformer activity, dispatched to the single chosen
    // node; that transformer brings its own [MemoryPackable] contract.
    /// <summary>
    /// Single-node delegation: dispatches the inner transformer to the ONE most
    /// suitable (fastest compatible) node and returns its single result. Like
    /// <see cref="WitActivityGridForEach"/> but without iteration — one activity,
    /// one node, one result. Used to run an inherently-sequential / heavy step
    /// (e.g. a simulation bake) on a capable worker before fanning the rest out.
    /// </summary>
    [Activity("Grid.Delegate")]
    public class WitActivityGridDelegate : WitActivityTransform
    {
        #region Constructors

        public WitActivityGridDelegate()
        {
        }

        #endregion

        #region Functions

        protected override string InnerString()
        {
            return Options == null
                ? string.Empty
                : $"{Options}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityGridDelegate activity)
                return false;

            return base.Is(activity, tolerance)
                   && Options.Check(activity.Options);
        }

        protected override WitActivityGridDelegate InnerClone()
        {
            return new WitActivityGridDelegate
            {
                Options = Options?.Clone() as IWitReference
            };
        }

        #endregion

        #region Properties

        public IWitReference? Options { get; init; }

        #endregion
    }
}
