using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("DateTime.Now")]
    [MemoryPackable]
    public sealed partial class WitActivityDateTimeNow : WitActivityFunction
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityDateTimeNow activity)
                return false;

            return base.Is(activity, tolerance);
        }

        protected override WitActivityDateTimeNow InnerClone()
        {
            return new WitActivityDateTimeNow();
        }

        #endregion
    }
}
