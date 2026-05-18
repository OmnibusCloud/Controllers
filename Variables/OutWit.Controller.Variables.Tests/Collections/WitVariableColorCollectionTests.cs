using System.Collections.Generic;
using OutWit.Common.Collections;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Variables.Collections;
using OutWit.Controller.Variables.Model;
using OutWit.Engine.Data.Serialization;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Collections
{
    [TestFixture]
    public class WitVariableColorCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableColorCollection variable = new WitVariableColorCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("ColorCollection:variable = [];"));

            variable = new WitVariableColorCollection("variable", [new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value.Is(new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)), Is.EqualTo(true));
            Assert.That(variable.ToString(), Is.EqualTo("ColorCollection:variable = [\"#FF3366AA\", \"#F03366AA\"];"));
        }

        [Test]
        public void IsTest()
        {
            WitVariableColorCollection variable = new WitVariableColorCollection("variable", [new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableColorCollection, IReadOnlyList<WitColor>>([new WitColor(51, 102, 170)])));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableColorCollection variable = new WitVariableColorCollection("variable", [new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)]);

            WitVariableColorCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value.Is([new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)]), Is.EqualTo(true));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableColorCollection variable = new WitVariableColorCollection("variable", [new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)]);

            WitVariableColorCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value.Is([new WitColor(51, 102, 170), new WitColor(51, 102, 170, 240)]), Is.EqualTo(true));

        }
    }
}
