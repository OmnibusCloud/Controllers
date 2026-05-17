using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Zip")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialZip : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Values == null || Values.Length == 0)
                return string.Empty;

            return string.Join(", ", Values.Select(parameter => $"{parameter}"));
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialZip activity)
                return false;

            return base.Is(activity, tolerance)
                   && Values.Is(activity.Values);
        }

        protected override WitActivitySpecialZip InnerClone()
        {
            return new WitActivitySpecialZip
            {
                Values = Values?.ArrayClone()
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter[]? Values { get; init; } 
        
        #endregion
    }
}
