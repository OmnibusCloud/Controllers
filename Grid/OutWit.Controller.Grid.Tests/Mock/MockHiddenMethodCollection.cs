using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A collection of objects whose method of the name Grid looks for is not public. Grid looks at
    /// public methods only: the collection is not told and nothing is said.
    /// </summary>
    [Variable("MockHiddenMethodCollection")]
    internal sealed class MockHiddenMethodCollection : WitCollection<object?>
    {
        #region Constructors

        public MockHiddenMethodCollection(string name)
            : base(name)
        {
        }

        #endregion

        #region Functions

        internal void OnGridAnswer(object? item, object? value)
        {
            Calls++;
        }

        #endregion

        #region ModelBase

        public override MockHiddenMethodCollection Clone()
        {
            return new MockHiddenMethodCollection(Name);
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
