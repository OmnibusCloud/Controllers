using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableProcessingOptionsTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableProcessingOptions("myOptions");
            Assert.That(variable.Name, Is.EqualTo("myOptions"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("ProcessingOptions:myOptions;"));

            variable = new WitVariableProcessingOptions("myOptions", new WitProcessingOptions{Strategy = ProcessingStrategy.Queued, MaxClients = 5});
            Assert.That(variable.Name, Is.EqualTo("myOptions"));
            Assert.That(variable.Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 5 }));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableProcessingOptions("myOptions", new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 5 });

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 6 })));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myOptions2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableProcessingOptions("myOptions", new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 5 });

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myOptions"));
            Assert.That(clone.Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 5 }));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableProcessingOptions("myOptions", new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 5 });

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myOptions"));
            Assert.That(clone.Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 5 }));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             ProcessingOptions:myOptions;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableProcessingOptions("myOptions")));
        }
    }
}