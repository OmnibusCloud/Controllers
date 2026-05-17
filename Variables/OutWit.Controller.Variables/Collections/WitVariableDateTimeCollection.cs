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
    [Variable("DateTimeCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableDateTimeCollection : WitCollection<DateTime?>, IWitVariableFactory<WitVariableDateTimeCollection>
    {
        #region Constructors

        public WitVariableDateTimeCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableDateTimeCollection(string name, IReadOnlyList<DateTime?> value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString(DateTime? value)
        {
            return $"\"{value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableDateTimeCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableDateTimeCollection Clone()
        {
            return new WitVariableDateTimeCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableDateTimeCollection Create(string name)
        {
            return new WitVariableDateTimeCollection(name);
        }

        #endregion
    }
}
