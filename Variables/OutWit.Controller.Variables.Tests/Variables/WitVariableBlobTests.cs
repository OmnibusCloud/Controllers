using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using System;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableBlobTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableBlob("myBlob");
            Assert.That(variable.Name, Is.EqualTo("myBlob"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("Blob:myBlob;"));

            variable = new WitVariableBlob("myBlob", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));
            Assert.That(variable.Name, Is.EqualTo("myBlob"));
            Assert.That(variable.Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
            Assert.That(variable.ToString(), Is.EqualTo("Blob:myBlob = \"bc12f10d-2b5b-4122-b7d0-ab99ccbbb3b7\";"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableBlob("myBlob", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableBlob, Guid?>(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B8}"))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myBlob2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableBlob("myBlob", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myBlob"));
            Assert.That(clone.Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableBlob("myBlob", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myBlob"));
            Assert.That(clone.Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
        }

        [Test]
        public void ParseVariableTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Blob:myBlob;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableBlob("myBlob")));
        }
    }
}
