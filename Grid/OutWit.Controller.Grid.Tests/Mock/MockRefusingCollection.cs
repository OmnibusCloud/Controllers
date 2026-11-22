using System.Collections.Generic;
using System.Linq;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection of integers that refuses every list and keeps the values it has. It stands for a
    /// collection that disagrees with what Grid expects it to take: the engine's own typed collection
    /// refuses only a list that holds a null or a value of another type.
    /// </summary>
    [Variable("RefusingIntCollection")]
    internal sealed class MockRefusingCollection : WitCollection<int>, IWitCollection
    {
        #region Constructors

        public MockRefusingCollection(string name, IReadOnlyList<int>? value = null)
            : base(name, value)
        {
        }

        #endregion

        #region IWitCollection

        bool IWitCollection.SetCollection<T>(IReadOnlyList<T> collection)
        {
            Refusals++;
            return false;
        }

        #endregion

        #region ModelBase

        public override MockRefusingCollection Clone()
        {
            return new MockRefusingCollection(Name, GetValue()?.ToArray());
        }

        #endregion

        #region Properties

        /// <summary>
        /// How many lists were offered and refused.
        /// </summary>
        public int Refusals { get; private set; }

        #endregion
    }
}
