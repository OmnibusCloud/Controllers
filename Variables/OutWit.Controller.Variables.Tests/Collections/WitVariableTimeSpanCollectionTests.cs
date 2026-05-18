using System;
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
    public class WitVariableTimeSpanCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableTimeSpanCollection variable = new WitVariableTimeSpanCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("TimeSpanCollection:variable = [];"));

            variable = new WitVariableTimeSpanCollection("variable", [new TimeSpan(6, 12, 14), new TimeSpan(10, 20, 30)]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.EqualTo([new TimeSpan(6, 12, 14), new TimeSpan(10, 20, 30)]));
        }

        [Test]
        public void IsTest()
        {
            WitVariableTimeSpanCollection variable = new WitVariableTimeSpanCollection("variable", [new TimeSpan(6, 12, 14), new TimeSpan(10, 20, 30)]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableTimeSpanCollection, IReadOnlyList<TimeSpan?>>([new TimeSpan(6, 12, 14)])));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableTimeSpanCollection variable = new WitVariableTimeSpanCollection("variable", [new TimeSpan(6, 12, 14), new TimeSpan(10, 20, 30)]);

            WitVariableTimeSpanCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableTimeSpanCollection variable = new WitVariableTimeSpanCollection("variable", [new TimeSpan(6, 12, 14), new TimeSpan(10, 20, 30)]);

            WitVariableTimeSpanCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
