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
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityGuidTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityGuid();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Guid();"));

            activity = new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Guid(\"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7\");"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Guid(\"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7\");"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B8")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityGuid;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityGuid;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Guid:myGuid = Guid("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("myGuid")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableGuid("myGuid")));
            
            script = """
                     Job:TestJob()
                     {
                         Guid:myGuid = "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("myGuid")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableGuid("myGuid")));

            script = """
                     Job:TestJob()
                     {
                         myGuid = Guid("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("myGuid")));

            script = """
                     Job:TestJob()
                     {
                         Guid("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Guid:myGuid1 = "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7";
                             Guid:myGuid2 = myGuid1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myGuid1"], Was.EqualTo(new WitVariableGuid("myGuid1")));
            Assert.That(job.Variables["myGuid2"], Was.EqualTo(new WitVariableGuid("myGuid2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityGuid
            {
                Value = (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"
            }.WithReturnReference("myGuid1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityGuid
            {
                Value = (WitReference)"myGuid1"
            }.WithReturnReference("myGuid2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Guid:myGuid = Guid();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityGuid>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Guid:myGuid = 23;
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityGuid>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myGuid = "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<IWitActivity>>(() => WitEngineSdk.Instance.Compile(script));
            
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Guid:myGuid = Guid("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myGuid"].Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));

            script = """
                     Job:TestJob()
                     {
                         Guid:myGuid = "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myGuid"].Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));

            script = """
                     Job:TestJob()
                     {
                         Guid:myGuid = "{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myGuid"].Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));

            script = """
                     Job:TestJob()
                     {
                         Guid:myGuid1 = "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7";
                         Guid:myGuid2 = myGuid1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myGuid1"].Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
            Assert.That(job.Variables["myGuid2"].Value, Is.EqualTo(Guid.Parse("{BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7}")));
        }
        
    }
}
