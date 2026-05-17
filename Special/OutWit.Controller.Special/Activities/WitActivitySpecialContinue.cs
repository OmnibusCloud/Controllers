using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
namespace OutWit.Controller.Special.Activities
{
    [Activity("Continue")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialContinue : WitActivityCommand
    {
        #region Constructors

        public WitActivitySpecialContinue()
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialContinue activity)
                return false;

            return base.Is(activity, tolerance);
        }

        public override WitActivitySpecialContinue Clone()
        {
            return new WitActivitySpecialContinue();
        }

        #endregion
    }
}
