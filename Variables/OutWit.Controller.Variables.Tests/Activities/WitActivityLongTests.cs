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
    public class WitActivityLongTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityLong();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Long();"));

            activity = new WitActivityLong
            {
                Value = (WitConstantNumeric)"10"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Long(10);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Long(10);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityLong
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantNumeric)"11")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityLong
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityLong;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityLong
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityLong;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Long:myLong = Long(23);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityLong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myLong")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableLong("myLong")));
            
            script = """
                     Job:TestJob()
                     {
                         Long:myLong = 23;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityLong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myLong")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableLong("myLong")));

            script = """
                     Job:TestJob()
                     {
                         myLong = Long(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityLong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myLong")));

            script = """
                     Job:TestJob()
                     {
                         Long(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityLong
            {
                Value = (WitConstantNumeric)"23"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Long:myLong1 = 23;
                             Long:myLong2 = myLong1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myLong1"], Was.EqualTo(new WitVariableLong("myLong1")));
            Assert.That(job.Variables["myLong2"], Was.EqualTo(new WitVariableLong("myLong2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityLong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myLong1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityLong
            {
                Value = (WitReference)"myLong1"
            }.WithReturnReference("myLong2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Long:myLong = Long();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityLong>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Long:myLong = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityLong>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myLong = 23;
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
                             Long:myLong = Long(23);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myLong"].Value, Is.EqualTo((long)23));

            script = """
                     Job:TestJob()
                     {
                         Long:myLong = 23;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myLong"].Value, Is.EqualTo((long)23));
            
            script = """
                     Job:TestJob()
                     {
                         Long:myLong1 = 23;
                         Long:myLong2 = myLong1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myLong1"].Value, Is.EqualTo((long)23));
            Assert.That(job.Variables["myLong2"].Value, Is.EqualTo((long)23));
        }
        
    }
}
