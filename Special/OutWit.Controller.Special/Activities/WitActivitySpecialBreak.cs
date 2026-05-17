using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
namespace OutWit.Controller.Special.Activities
{
    [Activity("Break")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialBreak : WitActivityCommand
    {
        #region Constructors

        public WitActivitySpecialBreak()
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialBreak activity)
                return false;

            return base.Is(activity, tolerance);
        }

        public override WitActivitySpecialBreak Clone()
        {
            return new WitActivitySpecialBreak();
        }

        #endregion
    }
}
