using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;

namespace OutWit.Controller.Variables.Variables
{
    /// <summary>
    /// Variable representing a reference to a blob in WitCloud storage.
    /// The underlying value is a <see cref="Guid"/> blob identifier.
    /// </summary>
    [Variable("Blob")]
    [MemoryPackable]
    public sealed partial class WitVariableBlob : WitVariable<Guid?>, IWitVariableFactory<WitVariableBlob>
    {
        #region Constructors

        public WitVariableBlob(string name)
             : base(name)
        {
        }

        [MemoryPackConstructor]
        public WitVariableBlob(string name, Guid? value)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        protected override string ValueString()
        {
            return $"\"{Value}\"";
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVariableBlob variable)
                return false;

            return base.Is(modelBase, tolerance)
                   && GetValue().Is(variable.GetValue());
        }

        public override WitVariableBlob Clone()
        {
            return new WitVariableBlob(Name, GetValue());
        }

        #endregion

        #region IWitVariableFactory

        public static WitVariableBlob Create(string name)
        {
            return new WitVariableBlob(name);
        }

        #endregion
    }
}
