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
    [Variable("DateTimeOffsetCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableDateTimeOffsetCollection : WitCollection<DateTimeOffset?>, IWitVariableFactory<WitVariableDateTimeOffsetCollection>
    {
        #region Constructors

        public WitVariableDateTimeOffsetCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDateTimeOffsetCollection(string name, IReadOnlyList<DateTimeOffset?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString(DateTimeOffset? value)
        {
            return $"\"{value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableDateTimeOffsetCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDateTimeOffsetCollection Clone()
        {
            return new WitVariableDateTimeOffsetCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDateTimeOffsetCollection Create(string name)
        {
            return new WitVariableDateTimeOffsetCollection(name);
        }

        #endregion
    }
}
