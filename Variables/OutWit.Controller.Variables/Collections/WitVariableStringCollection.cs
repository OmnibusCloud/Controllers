using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Collections
{
    [Variable("StringCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableStringCollection : WitCollection<string>, IWitVariableFactory<WitVariableStringCollection>
    {
        #region Constructors

        public WitVariableStringCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableStringCollection(string name, IReadOnlyList<string> value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString(string value)
        {
            return $"\"{value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableStringCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableStringCollection Clone()
        {
            return new WitVariableStringCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableStringCollection Create(string name)
        {
            return new WitVariableStringCollection(name);
        }

        #endregion
    }
}
