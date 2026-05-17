using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Variables.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OutWit.Controller.Variables.Collections
{
    [Variable("TimeSpanCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableTimeSpanCollection : WitCollection<TimeSpan?>, IWitVariableFactory<WitVariableTimeSpanCollection>
    {
        #region Constructors

        public WitVariableTimeSpanCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableTimeSpanCollection(string name, IReadOnlyList<TimeSpan?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString(TimeSpan? value)
        {
            return $"\"{value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableTimeSpanCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableTimeSpanCollection Clone()
        {
            return new WitVariableTimeSpanCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableTimeSpanCollection Create(string name)
        {
            return new WitVariableTimeSpanCollection(name);
        }

        #endregion
    }
}
