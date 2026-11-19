using System;
using System.Collections.Generic;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// What the return collection of a call takes: every value when it is a collection of objects or
    /// when there is none, a value of its element type otherwise. The rule has to agree with the
    /// engine's own collection, value for value: that is the last group of tests.
    /// </summary>
    [TestFixture]
    public class GridValueRuleTests
    {
        #region Constants

        private static readonly object?[] CANDIDATES =
        {
            null,
            5,
            5L,
            1.5,
            "text",
            Guid.Parse("7a1c2f0e-4c57-4a41-9c0e-2f2c1f6f0b11"),
            new object[] { 1, "two" },
            new string[] { "a", "b" },
            new int[] { 1, 2 },
            new List<int> { 1, 2 }
        };

        #endregion

        #region Tools

        private static bool EngineTakes(IWitCollection collection, object? value)
        {
            // Exactly what Grid.ForEach hands the engine: a list of objects.
            return collection.SetCollection(new List<object?> { value });
        }

        #endregion

        #region Element Type Tests

        [Test]
        public void ARuleOfNoTypeOrOfObjectsTakesEveryValueTest()
        {
            Assert.That(GridValueRule.ForElementType(null), Is.SameAs(GridValueRule.ANY));
            Assert.That(GridValueRule.ForElementType(typeof(object)), Is.SameAs(GridValueRule.ANY));
            Assert.That(GridValueRule.ANY.ElementType, Is.Null);

            foreach (object? value in CANDIDATES)
                Assert.That(GridValueRule.ANY.Accepts(value), Is.True, GridValueRule.Describe(value));
        }

        [Test]
        public void ARuleOfAValueTypeTakesThatTypeOnlyTest()
        {
            var rule = GridValueRule.ForElementType(typeof(int));

            Assert.That(rule.ElementType, Is.EqualTo(typeof(int)));
            Assert.That(rule.Accepts(5), Is.True);
            Assert.That(rule.Accepts(5L), Is.False, "a number of another type is not converted");
            Assert.That(rule.Accepts("5"), Is.False);
            Assert.That(rule.Accepts(null), Is.False);
        }

        [Test]
        public void ARuleOfAReferenceTypeRefusesNullAndTakesADerivedValueTest()
        {
            var rule = GridValueRule.ForElementType(typeof(Exception));

            Assert.That(rule.Accepts(new InvalidOperationException()), Is.True);
            Assert.That(rule.Accepts(null), Is.False, "a declared element type that allows null does not make the collection take one");
            Assert.That(rule.Accepts("text"), Is.False);
        }

        [Test]
        public void ARuleOfANullableValueTypeTakesTheValueAndRefusesNullTest()
        {
            var rule = GridValueRule.ForElementType(typeof(Guid?));

            Assert.That(rule.ElementType, Is.EqualTo(typeof(Guid)));
            Assert.That(rule.Accepts(Guid.NewGuid()), Is.True);
            Assert.That(rule.Accepts(null), Is.False);
            Assert.That(rule.Accepts("text"), Is.False);
        }

        #endregion

        #region Collection Tests

        [Test]
        public void TheRuleOfACollectionOfObjectsTakesEveryValueTest()
        {
            Assert.That(GridValueRule.ForCollection(new WitVariableObjectCollection("results")), Is.SameAs(GridValueRule.ANY));
            Assert.That(GridValueRule.ForCollection(new MockCollection<object?>("results")), Is.SameAs(GridValueRule.ANY));
        }

        [Test]
        public void TheRuleOfATypedCollectionIsItsElementTypeTest()
        {
            Assert.That(GridValueRule.ForCollection(new MockCollection<int>("results")).ElementType, Is.EqualTo(typeof(int)));
            Assert.That(GridValueRule.ForCollection(new MockCollection<string>("results")).ElementType, Is.EqualTo(typeof(string)));
            Assert.That(GridValueRule.ForCollection(new MockCollection<Guid?>("results")).ElementType, Is.EqualTo(typeof(Guid)));
            Assert.That(GridValueRule.ForCollection(new WitVariableTupleCollection("results")).ElementType, Is.EqualTo(typeof(object[])));
            Assert.That(GridValueRule.ForCollection(new MockRefusingCollection("results")).ElementType, Is.EqualTo(typeof(int)), "a collection derived further down is read the same way");
        }

        [Test]
        public void AVariableThatIsNotACollectionAndNoVariableHaveNoRuleTest()
        {
            Assert.That(GridValueRule.ForCollection(null), Is.SameAs(GridValueRule.ANY));
            Assert.That(GridValueRule.ForCollection(new WitVariableObject("result")), Is.SameAs(GridValueRule.ANY));
        }

        #endregion

        #region Describe Tests

        [Test]
        public void AValueIsDescribedByItsTypeAndANullAsNullTest()
        {
            Assert.That(GridValueRule.Describe(null), Is.EqualTo("null"));
            Assert.That(GridValueRule.Describe(5), Is.EqualTo("of type Int32"));
            Assert.That(GridValueRule.Describe("text"), Is.EqualTo("of type String"));
        }

        #endregion

        #region Engine Agreement Tests

        [Test]
        public void TheRuleTakesExactlyWhatTheEngineCollectionTakesTest()
        {
            var collections = new Func<IWitCollection>[]
            {
                () => new WitVariableObjectCollection("results"),
                () => new WitVariableTupleCollection("results"),
                () => new MockCollection<object?>("results"),
                () => new MockCollection<int>("results"),
                () => new MockCollection<long>("results"),
                () => new MockCollection<double>("results"),
                () => new MockCollection<string>("results"),
                () => new MockCollection<Guid?>("results"),
                () => new MockCollection<int[]>("results"),
                () => new MockCollection<IReadOnlyList<int>>("results")
            };

            foreach (var make in collections)
            {
                foreach (object? value in CANDIDATES)
                {
                    IWitCollection collection = make();
                    GridValueRule rule = GridValueRule.ForCollection(collection);

                    Assert.That(rule.Accepts(value), Is.EqualTo(EngineTakes(collection, value)),
                        $"{collection.GetType().Name} and a value {GridValueRule.Describe(value)}");
                }
            }
        }

        #endregion
    }
}
