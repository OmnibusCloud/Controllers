using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityProcessingOptionsTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityProcessingOptions();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Reference, Was.EqualTo(null));
            Assert.That(activity.Strategy, Was.EqualTo(null));
            Assert.That(activity.MaxClients, Was.EqualTo(null));

            activity = new WitActivityProcessingOptions
            {
                Reference = (WitReference)"options"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Reference, Was.EqualTo((WitReference)"options"));
            Assert.That(activity.Strategy, Was.EqualTo(null));
            Assert.That(activity.MaxClients, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("ProcessingOptions(options);"));


            activity = new WitActivityProcessingOptions
            {
                Strategy = (WitConstantString)"Queued",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Reference, Was.EqualTo(null));
            Assert.That(activity.Strategy, Was.EqualTo((WitConstantString)"Queued"));
            Assert.That(activity.MaxClients, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("ProcessingOptions(\"Queued\");"));

            activity = new WitActivityProcessingOptions
            {
                Strategy = (WitConstantString)"Queued",
                MaxClients = (WitConstantNumeric)"10",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Reference, Was.EqualTo(null));
            Assert.That(activity.Strategy, Was.EqualTo((WitConstantString)"Queued"));
            Assert.That(activity.MaxClients, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("ProcessingOptions(\"Queued\", 10);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = ProcessingOptions(\"Queued\", 10);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityProcessingOptions
            {
                Reference = (WitReference)"options",
                Strategy = (WitConstantString)"Queued",
                MaxClients = (WitConstantNumeric)"10",
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Reference, (WitReference)"options1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Strategy, (WitConstantString)"Queued1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.MaxClients, (WitConstantNumeric)"11")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityProcessingOptions
            {
                Reference = (WitReference)"options",
                Strategy = (WitConstantString)"Queued",
                MaxClients = (WitConstantNumeric)"10",
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityProcessingOptions;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Reference, Was.EqualTo((WitReference)"options"));
            Assert.That(clone.Strategy, Was.EqualTo((WitConstantString)"Queued"));
            Assert.That(clone.MaxClients, Was.EqualTo((WitConstantNumeric)"10"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityProcessingOptions
            {
                Reference = (WitReference)"options",
                Strategy = (WitConstantString)"Queued",
                MaxClients = (WitConstantNumeric)"10",
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityProcessingOptions;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Reference, Was.EqualTo((WitReference)"options"));
            Assert.That(clone.Strategy, Was.EqualTo((WitConstantString)"Queued"));
            Assert.That(clone.MaxClients, Was.EqualTo((WitConstantNumeric)"10"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             ProcessingOptions:myOptions = ProcessingOptions(options);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityProcessingOptions
            {
                Reference = (WitReference)"options"
            }.WithReturnReference("myOptions")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableProcessingOptions("myOptions")));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = options;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityProcessingOptions
            {
                Reference = (WitReference)"options"
            }.WithReturnReference("myOptions")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableProcessingOptions("myOptions")));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = "Queued";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityProcessingOptions
            {
                Strategy = (WitConstantString)"Queued"
            }.WithReturnReference("myOptions")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableProcessingOptions("myOptions")));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = ProcessingOptions("Queued");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityProcessingOptions
            {
                Strategy = (WitConstantString)"Queued"
            }.WithReturnReference("myOptions")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableProcessingOptions("myOptions")));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = ProcessingOptions("Queued", 10);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityProcessingOptions
            {
                Strategy = (WitConstantString)"Queued",
                MaxClients = (WitConstantNumeric)"10",
            }.WithReturnReference("myOptions")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableProcessingOptions("myOptions")));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions1 = "Balanced";
                         ProcessingOptions:myOptions2 = myOptions1;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myOptions1"], Was.EqualTo(new WitVariableProcessingOptions("myOptions1")));
            Assert.That(job.Variables["myOptions2"], Was.EqualTo(new WitVariableProcessingOptions("myOptions2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityProcessingOptions
            {
                Strategy = (WitConstantString)"Balanced"
            }.WithReturnReference("myOptions1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityProcessingOptions
            {
                Reference = (WitReference)"myOptions1"
            }.WithReturnReference("myOptions2")));

        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             ProcessingOptions:myOptions = ProcessingOptions();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityProcessingOptions>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             ProcessingOptions:myOptions = ProcessingOptions("Queued");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myOptions"].Value, Was.EqualTo(new WitProcessingOptions{Strategy = ProcessingStrategy.Queued}));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = "Queued";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myOptions"].Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued }));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = ProcessingOptions("Queued", 10);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myOptions"].Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, MaxClients = 10}));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions1 = "Queued";
                         ProcessingOptions:myOptions2 = myOptions1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myOptions1"].Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued }));
            Assert.That(job.Variables["myOptions2"].Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued }));

            script = """
                     Job:TestJob()
                     {
                         String:strategy = "Queued";
                         ProcessingOptions:myOptions2 = strategy;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["strategy"].Value, Was.EqualTo("Queued"));
            Assert.That(job.Variables["myOptions2"].Value, Was.EqualTo(new WitProcessingOptions { Strategy = ProcessingStrategy.Queued }));

        }
    }
}
