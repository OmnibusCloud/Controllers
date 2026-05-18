using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using System;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityNewGuidTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityNewGuid();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("NewGuid();"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = NewGuid();"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityNewGuid().WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityNewGuid().WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityNewGuid;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityNewGuid().WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityNewGuid;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Guid:myGuid = NewGuid();
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityNewGuid().WithReturnReference("myGuid")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableGuid("myGuid")));

            script = """
                     Job:TestJob()
                     {
                         myGuid = NewGuid();
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityNewGuid().WithReturnReference("myGuid")));

            script = """
                     Job:TestJob()
                     {
                         NewGuid();
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityNewGuid()));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Guid:myGuid = NewGuid(10);
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityNewGuid>>(() => WitEngineSdk.Instance.Compile(script));
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Guid:myGuid = NewGuid();
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myGuid"].Value, Is.Not.Null);
            Assert.That(job.Variables["myGuid"].Value, Is.Not.EqualTo(Guid.Empty));

            script = """
                     Job:TestJob()
                     {
                         NewGuid();
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
        }

        [Test]
        public void ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             myGuid = NewGuid();
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }
    }
}
