using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("ProcessingOptions")]
    [MemoryPackable]
    public sealed partial class WitActivityProcessingOptions : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Reference != null)
                return $"{Reference}";

            if (MaxClients != null)
                return $"{Strategy}, {MaxClients}";
            
            return $"{Strategy}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityProcessingOptions activity)
                return false;

            return base.Is(activity, tolerance)
                   && Reference.Check(activity.Reference)
                   && MaxClients.Check(activity.MaxClients)
                   && Strategy.Check(activity.Strategy);
        }

        protected override WitActivityProcessingOptions InnerClone()
        {
            return new WitActivityProcessingOptions
            {
                Reference = Reference?.Clone() as IWitReference,
                MaxClients = MaxClients?.Clone() as IWitParameter,
                Strategy = Strategy?.Clone() as IWitParameter,
            };
        }

        #endregion

        #region Properties
        
        [MemoryPackAllowSerialize]
        public IWitReference? Reference { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? MaxClients { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Strategy { get; init; }

        #endregion
    }
}
