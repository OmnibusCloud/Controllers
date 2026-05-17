using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using OutWit.Common.Values;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("Color")]
    [MemoryPackable]
    public sealed partial class WitActivityColor : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Value != null)
                return $"{Value}";
            
            return Alpha == null
                ? $"{Red}, {Green}, {Blue}"
                : $"{Red}, {Green}, {Blue}, {Alpha}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityColor activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value)
                   && Red.Check(activity.Red)
                   && Green.Check(activity.Green)
                   && Blue.Check(activity.Blue)
                   && Alpha.Check(activity.Alpha);
        }

        protected override WitActivityColor InnerClone()
        {
            return new WitActivityColor
            {
                Value = Value?.Clone() as IWitParameter,
                Red = Red?.Clone() as IWitParameter,
                Green = Green?.Clone() as IWitParameter,
                Blue = Blue?.Clone() as IWitParameter,
                Alpha = Alpha?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Value { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Red { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Green { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Blue { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Alpha { get; init; }

        #endregion
    }
}
