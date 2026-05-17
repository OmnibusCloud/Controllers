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
    [Variable("VectorSparse")]
    [MemoryPackable]
    public sealed partial class WitVariableVectorSparse : WitVariable<WitVectorSparse<double>?>, IWitVariableFactory<WitVariableVectorSparse>
    {
        #region Constructors

        public WitVariableVectorSparse(string name)
            : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableVectorSparse(string name, WitVectorSparse<double>? value)
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
            if (modelBase is not WitVariableVectorSparse variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && Value.Check(variable.Value);
        }

        public override WitVariableVectorSparse Clone()
        {
            return new WitVariableVectorSparse(Name, GetValue()?.Clone());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableVectorSparse Create(string name)
        {
            return new WitVariableVectorSparse(name);
        }

        #endregion
    }
}
