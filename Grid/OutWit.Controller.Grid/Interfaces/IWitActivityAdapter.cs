using System;
using OutWit.Common.Interfaces;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Interfaces
{
    internal interface IWitActivityAdapter<TActivity>
        where TActivity : IWitActivity
    {
        IResources Resources { get; }
    }
}
