using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Utils;
using System.Xml.Linq; 

namespace OutWit.Controller.Matrices.Tests.Variables
{
    [TestFixture]
    public class WitVariableMatrixSparseTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var elements = new List<(int r, int c, double val)>
            {
                (0, 1, 1.1),
                (0, 3, 2.2),
                (2, 2, 3.3),
                (3, 0, 4.4),
                (3, 4, 5.5)
            };
            
            var variable = new WitVariableMatrixSparse("myMatrix");
            Assert.That(variable.Name, Is.EqualTo("myMatrix"));
            Assert.That(variable.Value, Was.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("MatrixSparse:myMatrix;"));

            variable = new WitVariableMatrixSparse("myMatrix", WitMatrixSparse<double>.Create(4, 5, elements));
            Assert.That(variable.Name, Is.EqualTo("myMatrix"));
            Assert.That(variable.Value, Was.EqualTo((WitMatrixSparse<double>.Create(4, 5, elements))));
            Assert.That(variable.ToString(), Is.EqualTo("MatrixSparse:myMatrix = Sparse Matrix (4x5), 5 non-zero elements;"));
        }

        [Test]
        public void IsTest()
        {
            var elements = new List<(int r, int c, double val)>
            {
                (0, 1, 1.1),
                (0, 3, 2.2),
                (2, 2, 3.3),
                (3, 0, 4.4),
                (3, 4, 5.5)
            };
            var variable = new WitVariableMatrixSparse("myMatrix", WitMatrixSparse<double>.Create(4, 5, elements));

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((WitMatrixSparse<double>.Create(5, 5, elements)))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myMatrix2")));
        }


        [Test]
        public void CloneTest()
        {
            var elements = new List<(int r, int c, double val)>
            {
                (0, 1, 1.1),
                (0, 3, 2.2),
                (2, 2, 3.3),
                (3, 0, 4.4),
                (3, 4, 5.5)
            };
            
            var variable = new WitVariableMatrixSparse("myMatrix", WitMatrixSparse<double>.Create(4, 5, elements));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myMatrix"));
            Assert.That(clone.Value, Was.EqualTo((WitMatrixSparse<double>.Create(4, 5, elements))));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var elements = new List<(int r, int c, double val)>
            {
                (0, 1, 1.1),
                (0, 3, 2.2),
                (2, 2, 3.3),
                (3, 0, 4.4),
                (3, 4, 5.5)
            };
            
            var variable = new WitVariableMatrixSparse("myMatrix", WitMatrixSparse<double>.Create(4, 5, elements));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myMatrix"));
            Assert.That(clone.Value, Was.EqualTo((WitMatrixSparse<double>.Create(4, 5, elements))));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             MatrixSparse:myMatrix;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrixSparse("myMatrix")));
        }
    }
}