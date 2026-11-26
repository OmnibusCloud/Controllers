using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection of objects whose public method of the name Grid looks for is static: not a
    /// method of the collection a call returns into, so Grid says so once and does not call it.
    /// </summary>
    [Variable("MockStaticMethodCollection")]
    internal sealed class MockStaticMethodCollection : WitCollection<object?>
    {
        #region Constructors

        public MockStaticMethodCollection(string name)
            : base(name)
        {
        }

        #endregion

        #region Functions

        /// <summary>
        /// Not what Grid calls: it belongs to no collection.
        /// </summary>
        /// <param name="item">An item.</param>
        /// <param name="value">A value.</param>
        public static void OnGridAnswer(object? item, object? value)
        {
            Calls++;
        }

        #endregion

        #region ModelBase

        public override MockStaticMethodCollection Clone()
        {
            return new MockStaticMethodCollection(Name);
        }

        #endregion

        #region Properties

        /// <summary>
        /// How many times the method was called.
        /// </summary>
        public static int Calls { get; private set; }

        #endregion
    }
}
