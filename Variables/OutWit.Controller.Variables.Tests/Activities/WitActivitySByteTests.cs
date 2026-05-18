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
    public class WitActivitySByteTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySByte();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("SByte();"));

            activity = new WitActivitySByte
            {
                Value = (WitConstantNumeric)"10"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("SByte(10);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = SByte(10);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySByte
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
            var activity = new WitActivitySByte
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivitySByte;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySByte
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivitySByte;

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
                             SByte:mySByte = SByte(23);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByte
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("mySByte")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableSByte("mySByte")));
            
            script = """
                     Job:TestJob()
                     {
                         SByte:mySByte = 23;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByte
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("mySByte")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableSByte("mySByte")));

            script = """
                     Job:TestJob()
                     {
                         mySByte = SByte(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByte
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("mySByte")));

            script = """
                     Job:TestJob()
                     {
                         SByte(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByte
            {
                Value = (WitConstantNumeric)"23"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             SByte:mySByte1 = 23;
                             SByte:mySByte2 = mySByte1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["mySByte1"], Was.EqualTo(new WitVariableSByte("mySByte1")));
            Assert.That(job.Variables["mySByte2"], Was.EqualTo(new WitVariableSByte("mySByte2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivitySByte
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("mySByte1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivitySByte
            {
                Value = (WitReference)"mySByte1"
            }.WithReturnReference("mySByte2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         SByte:mySByte = SByte();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivitySByte>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         SByte:mySByte = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivitySByte>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         mySByte = 23;
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
                             SByte:mySByte = SByte(23);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["mySByte"].Value, Is.EqualTo((sbyte)23));

            script = """
                     Job:TestJob()
                     {
                         SByte:mySByte = 23;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["mySByte"].Value, Is.EqualTo((sbyte)23));
            
            script = """
                     Job:TestJob()
                     {
                         SByte:mySByte1 = 23;
                         SByte:mySByte2 = mySByte1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["mySByte1"].Value, Is.EqualTo((sbyte)23));
            Assert.That(job.Variables["mySByte2"].Value, Is.EqualTo((sbyte)23));
        }
        
    }
}
