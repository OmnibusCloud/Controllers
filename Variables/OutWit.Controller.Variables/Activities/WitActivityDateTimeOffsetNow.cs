using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("DateTimeOffset.Now")]
    [MemoryPackable]
    public sealed partial class WitActivityDateTimeOffsetNow : WitActivityFunction
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityDateTimeOffsetNow activity)
                return false;

            return base.Is(activity, tolerance);
        }

        protected override WitActivityDateTimeOffsetNow InnerClone()
        {
            return new WitActivityDateTimeOffsetNow();
        }

        #endregion
    }
}
