using System.Collections.Generic;
using System.Linq;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A typed collection as the engine makes them, with nothing of its own: what it takes and what it
    /// refuses is the engine's rule for every collection of that element type.
    /// </summary>
    /// <typeparam name="TValue">The element type.</typeparam>
    [Variable("MockCollection")]
    internal sealed class MockCollection<TValue> : WitCollection<TValue>
    {
        #region Constructors

        public MockCollection(string name, IReadOnlyList<TValue>? value = null)
            : base(name, value)
        {
        }

        #endregion

        #region ModelBase

        public override MockCollection<TValue> Clone()
        {
            return new MockCollection<TValue>(Name, GetValue()?.ToArray());
        }

        #endregion
    }
}
