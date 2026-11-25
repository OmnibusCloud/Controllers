using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection of objects whose public method of the name Grid looks for returns a verdict:
    /// another signature than the one Grid calls (Grid does nothing with an answer), so Grid says so
    /// once and does not call it.
    /// </summary>
    [Variable("MockAnsweringBackCollection")]
    internal sealed class MockAnsweringBackCollection : WitCollection<object?>
    {
        #region Constructors

        public MockAnsweringBackCollection(string name)
            : base(name)
        {
        }

        #endregion

        #region Functions

        /// <summary>
        /// Not what Grid calls: it returns something.
        /// </summary>
        /// <param name="item">An item.</param>
        /// <param name="value">A value.</param>
        /// <returns>Always true.</returns>
        public bool OnGridAnswer(object? item, object? value)
        {
            Calls++;
            return true;
        }

        #endregion

        #region ModelBase

        public override MockAnsweringBackCollection Clone()
        {
            return new MockAnsweringBackCollection(Name);
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
