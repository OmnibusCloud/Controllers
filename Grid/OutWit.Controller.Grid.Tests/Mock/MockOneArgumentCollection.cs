using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection of objects whose public method of the name Grid looks for takes the value alone:
    /// another signature than the one Grid calls, so Grid says so once and does not call it.
    /// </summary>
    [Variable("MockOneArgumentCollection")]
    internal sealed class MockOneArgumentCollection : WitCollection<object?>
    {
        #region Constructors

        public MockOneArgumentCollection(string name)
            : base(name)
        {
        }

        #endregion

        #region Functions

        /// <summary>
        /// Not what Grid calls: one argument.
        /// </summary>
        /// <param name="value">A value.</param>
        public void OnGridAnswer(object? value)
        {
            Calls++;
        }

        #endregion

        #region ModelBase

        public override MockOneArgumentCollection Clone()
        {
            return new MockOneArgumentCollection(Name);
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
