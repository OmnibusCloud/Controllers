using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;
using System;
using MemoryPack;
using OutWit.Common.Values;

namespace OutWit.Controller.Variables.Activities
{
    [Activity("DateTime")]
    [MemoryPackable]
    public sealed partial class WitActivityDateTime : WitActivityFunction
    {
        #region Functions

        protected override string InnerString()
        {
            if (Value != null)
                return $"{Value}";
            return Hour == null
                ? $"{Year}, {Month}, {Day}"
                : $"{Year}, {Month}, {Day}, {Hour}, {Minute}, {Second}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitActivityDateTime activity)
                return false;

            return base.Is(activity, tolerance)
                   && Value.Check(activity.Value)
                   && Year.Check(activity.Year)
                   && Month.Check(activity.Month)
                   && Day.Check(activity.Day)
                   && Hour.Check(activity.Hour)
                   && Minute.Check(activity.Minute)
                   && Second.Check(activity.Second);
        }

        protected override WitActivityDateTime InnerClone()
        {
            return new WitActivityDateTime
            {
                Value = Value?.Clone() as IWitParameter,
                Year = Year?.Clone() as IWitParameter,
                Month = Month?.Clone() as IWitParameter,
                Day = Day?.Clone() as IWitParameter,
                Hour = Hour?.Clone() as IWitParameter,
                Minute = Minute?.Clone() as IWitParameter,
                Second = Second?.Clone() as IWitParameter
            };
        }

        #endregion

        #region Properies

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

        #endregion
    }
}
