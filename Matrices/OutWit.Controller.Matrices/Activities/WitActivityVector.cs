using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Activities
{
    [Activity("Vector")]
    [MemoryPackable]
    public sealed partial class WitActivityVector : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Data == null)
                return "";

            if (Type == null)
                return $"{Data}";

            return $"{Data}, {Type}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityVector activity)
                return false;

            return base.Is(activity, tolerance)
                   && Data.Check(activity.Data)
                   && Type.Check(activity.Type);
        }

        protected override WitActivityVector InnerClone()
        {
            return new WitActivityVector
            {
                Data = Data?.Clone() as IWitParameter,
                Type = Type?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Data { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Type { get; init; }

        #endregion
    }
}
