using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("NewGuid")]
    [MemoryPackable]
    public sealed partial class WitActivityNewGuid : WitActivityFunction
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityNewGuid activity)
                return false;

            return base.Is(activity, tolerance);
        }

        protected override WitActivityNewGuid InnerClone()
        {
            return new WitActivityNewGuid();
        }

        #endregion
    }
}
