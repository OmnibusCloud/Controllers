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
    [Variable("Matrix")]
    [MemoryPackable]
    public sealed partial class WitVariableMatrix : WitVariable<WitMatrix<double>?>, IWitVariableFactory<WitVariableMatrix>
    {
        #region Constructors

        public WitVariableMatrix(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableMatrix(string name, WitMatrix<double>? value)
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
            if (modelBase is not WitVariableMatrix variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && Value.Check(variable.Value);
        }

        public override WitVariableMatrix Clone()
        {
            return new WitVariableMatrix(Name, GetValue()?.Clone());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableMatrix Create(string name)
        {
            return new WitVariableMatrix(name);
        }

        #endregion
    }
}
