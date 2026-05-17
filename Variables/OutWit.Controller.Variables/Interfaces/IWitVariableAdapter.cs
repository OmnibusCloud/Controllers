using System;
using OutWit.Common.Interfaces;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Interfaces
{
    internal interface IWitVariableAdapter<TVariable>
        where TVariable : IWitVariable
    {
        IResources Resources { get; }
    }
}
