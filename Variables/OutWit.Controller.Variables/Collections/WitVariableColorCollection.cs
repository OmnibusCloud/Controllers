using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Variables.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Collections
{
    [Variable("ColorCollection")]
    [MemoryPackable]
    public sealed partial class WitVariableColorCollection : WitCollection<WitColor>, IWitVariableFactory<WitVariableColorCollection>
    {
        #region Constructors

        public WitVariableColorCollection(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableColorCollection(string name, IReadOnlyList<WitColor> value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString(WitColor value)
        {
            return $"\"{value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableColorCollection variable)
                return false;

            return base.Is(modelBase, tolerance) 
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableColorCollection Clone()
        {
            return new WitVariableColorCollection(Name, GetValue()?.ToArray() ?? []);
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableColorCollection Create(string name)
        {
            return new WitVariableColorCollection(name);
        }

        #endregion
    }
}
