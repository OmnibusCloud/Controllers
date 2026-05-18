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
    public class WitVariableVectorSparseCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            var vector1 = WitVectorSparse<double>.Create(5, new[] { (1, 1.1), (3, 3.3) });
            var vector2 = WitVectorSparse<double>.Create(7, new[] { (2, 2.2), (4, 4.4), (6, 6.6) });
            
            WitVariableVectorSparseCollection variable = new WitVariableVectorSparseCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("VectorSparseCollection:variable = [];"));

            variable = new WitVariableVectorSparseCollection("variable", [vector1, vector2]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value.Is(vector1, vector2), Is.EqualTo(true));
        }

        [Test]
        public void IsTest()
        {
            var vector1 = WitVectorSparse<double>.Create(5, new[] { (1, 1.1), (3, 3.3) });
            var vector2 = WitVectorSparse<double>.Create(7, new[] { (2, 2.2), (4, 4.4), (6, 6.6) });
            
            WitVariableVectorSparseCollection variable = new WitVariableVectorSparseCollection("variable", [vector1, vector2]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableVectorSparseCollection, IReadOnlyList<WitVectorSparse<double>?>>(new List<WitVectorSparse<double>?> {vector1})));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            var vector1 = WitVectorSparse<double>.Create(5, new[] { (1, 1.1), (3, 3.3) });
            var vector2 = WitVectorSparse<double>.Create(7, new[] { (2, 2.2), (4, 4.4), (6, 6.6) });
            
            WitVariableVectorSparseCollection variable = new WitVariableVectorSparseCollection("variable", [vector1, vector2]);

            WitVariableVectorSparseCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var vector1 = WitVectorSparse<double>.Create(5, new[] { (1, 1.1), (3, 3.3) });
            var vector2 = WitVectorSparse<double>.Create(7, new[] { (2, 2.2), (4, 4.4), (6, 6.6) });
            
            WitVariableVectorSparseCollection variable = new WitVariableVectorSparseCollection("variable", [vector1, vector2]);

            WitVariableVectorSparseCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
