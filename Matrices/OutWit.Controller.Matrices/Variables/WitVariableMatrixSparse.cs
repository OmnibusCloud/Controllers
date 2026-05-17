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
    [Variable("MatrixSparse")]
    [MemoryPackable]
    public sealed partial class WitVariableMatrixSparse : WitVariable<WitMatrixSparse<double>?>, IWitVariableFactory<WitVariableMatrixSparse>
    {
        #region Constructors

        public WitVariableMatrixSparse(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableMatrixSparse(string name, WitMatrixSparse<double>? value)
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
            if (modelBase is not WitVariableMatrixSparse variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && Value.Check(variable.Value);
        }

        public override WitVariableMatrixSparse Clone()
        {
            return new WitVariableMatrixSparse(Name, GetValue()?.Clone());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableMatrixSparse Create(string name)
        {
            return new WitVariableMatrixSparse(name);
        }

        #endregion
    }
}
