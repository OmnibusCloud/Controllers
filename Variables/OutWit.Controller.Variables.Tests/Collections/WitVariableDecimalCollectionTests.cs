using System.Collections.Generic;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Variables.Collections;
using OutWit.Engine.Data.Serialization;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Variables.Tests.Collections
{
    [TestFixture]
    public class WitVariableDecimalCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableDecimalCollection variable = new WitVariableDecimalCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("DecimalCollection:variable = [];"));

            variable = new WitVariableDecimalCollection("variable", [1, 2, 3]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.EqualTo([1, 2, 3]));
            Assert.That(variable.ToString(), Is.EqualTo("DecimalCollection:variable = [1, 2, 3];"));
        }

        [Test]
        public void IsTest()
        {
            WitVariableDecimalCollection variable = new WitVariableDecimalCollection("variable", [1, 2, 3]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableDecimalCollection, IReadOnlyList<decimal>>([1, 2])));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableDecimalCollection variable = new WitVariableDecimalCollection("variable", [1, 2, 3]);

            WitVariableDecimalCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableDecimalCollection variable = new WitVariableDecimalCollection("variable", [1, 2, 3]);

            WitVariableDecimalCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
