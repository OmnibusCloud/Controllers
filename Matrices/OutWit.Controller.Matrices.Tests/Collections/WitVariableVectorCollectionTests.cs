using System.Collections.Generic;
using OutWit.Common.Collections;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Collections;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Data.Serialization;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Matrices.Tests.Collections
{
    [TestFixture]
    public class WitVariableVectorCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableVectorCollection variable = new WitVariableVectorCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("VectorCollection:variable = [];"));

            variable = new WitVariableVectorCollection("variable", [WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.6])]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value.Is(WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.6])), Is.EqualTo(true));
            Assert.That(variable.ToString(), Is.EqualTo("VectorCollection:variable = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];"));
        }

        [Test]
        public void IsTest()
        {
            WitVariableVectorCollection variable = new WitVariableVectorCollection("variable", [WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.6])]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableVectorCollection, IReadOnlyList<WitVector<double>?>>(new List<WitVector<double>?> { WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.7]) })));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableVectorCollection variable = new WitVariableVectorCollection("variable", [WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.6])]);

            WitVariableVectorCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableVectorCollection variable = new WitVariableVectorCollection("variable", [WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.6])]);

            WitVariableVectorCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
