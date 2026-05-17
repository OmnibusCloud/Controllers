using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("DateTimeOffset")]
    [MemoryPackable]
    public sealed partial class WitActivityDateTimeOffset : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Value != null)
                return $"{Value}";
            if (Hour == null)
                return $"{Year}, {Month}, {Day}";
            if (Offset == null)
                return $"{Year}, {Month}, {Day}, {Hour}, {Minute}, {Second}";

            return $"{Year}, {Month}, {Day}, {Hour}, {Minute}, {Second}, {Offset}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityDateTimeOffset activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value)
                   && Year.Check(activity.Year)
                   && Month.Check(activity.Month)
                   && Day.Check(activity.Day)
                   && Hour.Check(activity.Hour)
                   && Minute.Check(activity.Minute)
                   && Second.Check(activity.Second)
                   && Offset.Check(activity.Offset);
        }

        protected override WitActivityDateTimeOffset InnerClone()
        {
            return new WitActivityDateTimeOffset
            {
                Value = Value?.Clone() as IWitParameter,
                Year = Year?.Clone() as IWitParameter,
                Month = Month?.Clone() as IWitParameter,
                Day = Day?.Clone() as IWitParameter,
                Hour = Hour?.Clone() as IWitParameter,
                Minute = Minute?.Clone() as IWitParameter,
                Second = Second?.Clone() as IWitParameter,
                Offset = Offset?.Clone() as IWitReference
            };
        }

        #endregion

        #region Properties

        [MemoryPackAllowSerialize]
        public IWitParameter? Value { get; init; }

        [MemoryPackAllowSerialize]
        public IWitParameter? Year { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Month { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Day { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Hour { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Minute { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitParameter? Second { get; init; }
        
        [MemoryPackAllowSerialize]
        public IWitReference? Offset { get; init; }

        #endregion
    }
}
