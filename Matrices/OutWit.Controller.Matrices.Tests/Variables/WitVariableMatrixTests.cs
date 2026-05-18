using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Sdk;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Matrices.Tests.Variables
{
    [TestFixture]
    public class WitVariableMatrixTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableMatrix("myMatrix");
            Assert.That(variable.Name, Is.EqualTo("myMatrix"));
            Assert.That(variable.Value, Was.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("Matrix:myMatrix;"));

            variable = new WitVariableMatrix("myMatrix", new[,] { {1.1, 2.2, 3.3}, {4.4, 5.5, 6.6} });
            Assert.That(variable.Name, Is.EqualTo("myMatrix"));
            Assert.That(variable.Value, Was.EqualTo((WitMatrix<double>)new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } }));
            Assert.That(variable.ToString(), Is.EqualTo("Matrix:myMatrix = [[ 1.1, 2.2, 3.3 ][ 4.4, 5.5, 6.6 ]];"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableMatrix("myMatrix", new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } });

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((WitMatrix<double>)new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.7 } })));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myMatrix2")));
        }


        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableMatrix("myMatrix", new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } });

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myMatrix"));
            Assert.That(clone.Value, Was.EqualTo((WitMatrix<double>)new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } }));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableMatrix("myMatrix", new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } });

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myMatrix"));
            Assert.That(clone.Value, Was.EqualTo((WitMatrix<double>)new[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } }));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Matrix:myMatrix;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrix("myMatrix")));
        }
    }
}