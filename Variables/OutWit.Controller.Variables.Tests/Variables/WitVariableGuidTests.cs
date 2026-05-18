using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using System;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableGuidTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableGuid("myGuid");
            Assert.That(variable.Name, Is.EqualTo("myGuid"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("Guid:myGuid;"));

            variable = new WitVariableGuid("myGuid", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));
            Assert.That(variable.Name, Is.EqualTo("myGuid"));
            Assert.That(variable.Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
            Assert.That(variable.ToString(), Is.EqualTo("Guid:myGuid = \"bc12f10d-2b5b-4122-b7d0-ab99ccbbb3b7\";"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableGuid("myGuid", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableGuid, Guid?>(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B8}"))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myGuid2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableGuid("myGuid", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myGuid"));
            Assert.That(clone.Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableGuid("myGuid", Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}"));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myGuid"));
            Assert.That(clone.Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Guid:myGuid;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableGuid("myGuid")));
        }
    }
}