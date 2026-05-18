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
    public class WitVariableShortCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableShortCollection variable = new WitVariableShortCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("ShortCollection:variable = [];"));

            variable = new WitVariableShortCollection("variable", [1, 2, 3]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.EqualTo([1, 2, 3]));
            Assert.That(variable.ToString(), Is.EqualTo("ShortCollection:variable = [1, 2, 3];"));
        }

        [Test]
        public void IsTest()
        {
            WitVariableShortCollection variable = new WitVariableShortCollection("variable", [1, 2, 3]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableShortCollection, IReadOnlyList<short>>([1, 2])));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableShortCollection variable = new WitVariableShortCollection("variable", [1, 2, 3]);

            WitVariableShortCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableShortCollection variable = new WitVariableShortCollection("variable", [1, 2, 3]);

            WitVariableShortCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
