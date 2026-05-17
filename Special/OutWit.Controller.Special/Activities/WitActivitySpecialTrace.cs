using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Activities
{
    [Activity("Trace")]
    [MemoryPackable]
    public sealed partial class WitActivitySpecialTrace : WitActivityCommand
    {
        #region Constructors

        public WitActivitySpecialTrace()
        {
        }
        
        #endregion

        #region Functions

        protected override string InnerString()
        {
            return ThrowException == null 
                ? $"{Message}" 
                : $"{Message}, {ThrowException}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivitySpecialTrace activity)
                return false;

            return base.Is(activity, tolerance)
                   && Message.Check(activity.Message)
                   && ThrowException.Check(activity.ThrowException);
        }

        public override WitActivitySpecialTrace Clone()
        {
            return new WitActivitySpecialTrace
            {
                Message = Message?.Clone() as IWitParameter,
                ThrowException = ThrowException?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Message { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? ThrowException { get; init; }

        #endregion
    }
}
