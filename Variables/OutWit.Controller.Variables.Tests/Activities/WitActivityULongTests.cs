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
    public class WitActivityULongTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityULong();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("ULong();"));

            activity = new WitActivityULong
            {
                Value = (WitConstantNumeric)"10"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("ULong(10);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = ULong(10);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityULong
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
            var activity = new WitActivityULong
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityULong;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityULong
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityULong;

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
                             ULong:myULong = ULong(23);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityULong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myULong")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableULong("myULong")));
            
            script = """
                     Job:TestJob()
                     {
                         ULong:myULong = 23;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityULong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myULong")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableULong("myULong")));

            script = """
                     Job:TestJob()
                     {
                         myULong = ULong(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityULong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myULong")));

            script = """
                     Job:TestJob()
                     {
                         ULong(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityULong
            {
                Value = (WitConstantNumeric)"23"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             ULong:myULong1 = 23;
                             ULong:myULong2 = myULong1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myULong1"], Was.EqualTo(new WitVariableULong("myULong1")));
            Assert.That(job.Variables["myULong2"], Was.EqualTo(new WitVariableULong("myULong2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityULong
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myULong1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityULong
            {
                Value = (WitReference)"myULong1"
            }.WithReturnReference("myULong2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         ULong:myULong = ULong();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityULong>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         ULong:myULong = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityULong>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myULong = 23;
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
                             ULong:myULong = ULong(23);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myULong"].Value, Is.EqualTo((ulong)23));

            script = """
                     Job:TestJob()
                     {
                         ULong:myULong = 23;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myULong"].Value, Is.EqualTo((ulong)23));
            
            script = """
                     Job:TestJob()
                     {
                         ULong:myULong1 = 23;
                         ULong:myULong2 = myULong1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myULong1"].Value, Is.EqualTo((ulong)23));
            Assert.That(job.Variables["myULong2"].Value, Is.EqualTo((ulong)23));
        }

        [Test]
        public void ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             ULong:myULong = -23;
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
