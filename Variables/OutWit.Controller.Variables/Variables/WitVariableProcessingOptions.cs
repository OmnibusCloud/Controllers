using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Variables
{
    [Variable("ProcessingOptions")]
    [MemoryPackable]
    public sealed partial class WitVariableProcessingOptions : WitVariable<WitProcessingOptions?>, IWitVariableFactory<WitVariableProcessingOptions>
    {
        #region Constructors

        public WitVariableProcessingOptions(string name) 
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableProcessingOptions(string name, WitProcessingOptions? value)
            : base(name, value)
        {
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableProcessingOptions variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Check(variable.GetValue());
        }

        public override WitVariableProcessingOptions Clone()
        {
            return new WitVariableProcessingOptions(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableProcessingOptions Create(string name)
        {
            return new WitVariableProcessingOptions(name);
        }

        #endregion
    }
}
