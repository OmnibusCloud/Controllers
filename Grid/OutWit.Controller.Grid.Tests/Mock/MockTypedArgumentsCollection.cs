using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection of objects whose public method of the name Grid looks for takes an item and a
    /// value of its own types: another signature than the one Grid calls, so Grid says so once and
    /// does not call it.
    /// </summary>
    [Variable("MockTypedArgumentsCollection")]
    internal sealed class MockTypedArgumentsCollection : WitCollection<object?>
    {
        #region Constructors

        public MockTypedArgumentsCollection(string name)
            : base(name)
        {
        }

        #endregion

        #region Functions

        /// <summary>
        /// Not what Grid calls: typed arguments.
        /// </summary>
        /// <param name="item">An item.</param>
        /// <param name="value">A value.</param>
        public void OnGridAnswer(string item, int value)
        {
            Calls++;
        }

        #endregion

        #region ModelBase

        public override MockTypedArgumentsCollection Clone()
        {
            return new MockTypedArgumentsCollection(Name);
        }

        #endregion

        #region Properties

        /// <summary>
        /// How many times the method was called.
        /// </summary>
        public int Calls { get; private set; }

        #endregion
    }
}
