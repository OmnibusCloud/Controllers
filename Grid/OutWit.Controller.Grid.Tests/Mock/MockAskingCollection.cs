using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A typed collection that asks to be told which item each value answers: it declares the method
    /// Grid looks for and keeps every pair it is told. A test may make the method throw for a pair;
    /// a pair it throws for is not kept, as a controller that threw has not taken it in.
    /// </summary>
    /// <typeparam name="TValue">The element type.</typeparam>
    [Variable("MockAskingCollection")]
    internal class MockAskingCollection<TValue> : WitCollection<TValue>
    {
        #region Fields

        private readonly List<(object? Item, object? Value)> m_told = new();

        #endregion

        #region Constructors

        public MockAskingCollection(string name, IReadOnlyList<TValue>? value = null)
            : base(name, value)
        {
        }

        #endregion

        #region Functions

        /// <summary>
        /// What Grid calls for every value it keeps.
        /// </summary>
        /// <param name="item">The item of the task the value was returned for.</param>
        /// <param name="value">The value.</param>
        /// <exception cref="Exception">Whatever <see cref="Throws"/> gives for the pair.</exception>
        public void OnGridAnswer(object? item, object? value)
        {
            Exception? exception = Throws?.Invoke(item, value);
            if (exception != null)
                throw exception;

            lock (m_told)
                m_told.Add((item, value));
        }

        #endregion

        #region ModelBase

        public override MockAskingCollection<TValue> Clone()
        {
            return new MockAskingCollection<TValue>(Name, GetValue()?.ToArray());
        }

        #endregion

        #region Properties

        /// <summary>
        /// The pairs the collection was told, in the order they came.
        /// </summary>
        public IReadOnlyList<(object? Item, object? Value)> Told
        {
            get
            {
                lock (m_told)
                    return m_told.ToList();
            }
        }

        /// <summary>
        /// Says what the method throws for a pair; null for a pair it takes. Null when it never throws.
        /// </summary>
        public Func<object?, object?, Exception?>? Throws { get; set; }

        #endregion
    }
}
