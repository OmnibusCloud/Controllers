using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection that asks to be told through the method of the class it derives from, and has a
    /// second public method of that name with other arguments beside it. The method Grid calls is
    /// there, so the collection is told through it and nothing is said of the other.
    /// </summary>
    [Variable("MockAskingDerivedCollection")]
    internal sealed class MockAskingDerivedCollection : MockAskingCollection<object?>
    {
        #region Constructors

        public MockAskingDerivedCollection(string name)
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
            OtherCalls++;
        }

        #endregion

        #region ModelBase

        public override MockAskingDerivedCollection Clone()
        {
            return new MockAskingDerivedCollection(Name);
        }

        #endregion

        #region Properties

        /// <summary>
        /// How many times the method with other arguments was called.
        /// </summary>
        public int OtherCalls { get; private set; }

        #endregion
    }
}
