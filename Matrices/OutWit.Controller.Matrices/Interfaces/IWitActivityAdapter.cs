using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Common.Interfaces;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Interfaces
{
    internal interface IWitActivityAdapter<TActivity>
        where TActivity : IWitActivity
    {
        IResources Resources { get; }
    }
}
