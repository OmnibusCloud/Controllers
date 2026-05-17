using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Variables
{
    [Variable("Vector")]
    [MemoryPackable]
    public sealed partial class WitVariableVector : WitVariable<WitVector<double>?>, IWitVariableFactory<WitVariableVector>
    {
        #region Constructors

        public WitVariableVector(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableVector(string name, WitVector<double>? value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString()
        {
            return $"{Value}";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableVector variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && Value.Check(variable.Value);
        }

        public override WitVariableVector Clone()
        {
            return new WitVariableVector(Name, GetValue()?.Clone());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableVector Create(string name)
        {
            return new WitVariableVector(name);
        }

        #endregion
    }
}
