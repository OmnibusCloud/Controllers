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
    public class WitActivityShortTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityShort();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Short();"));

            activity = new WitActivityShort
            {
                Value = (WitConstantNumeric)"10"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Short(10);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Short(10);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityShort
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
            var activity = new WitActivityShort
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityShort;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityShort
            {
                Value = (WitConstantNumeric)"10"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityShort;

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
                             Short:myShort = Short(23);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityShort
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myShort")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableShort("myShort")));
            
            script = """
                     Job:TestJob()
                     {
                         Short:myShort = 23;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityShort
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myShort")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableShort("myShort")));

            script = """
                     Job:TestJob()
                     {
                         myShort = Short(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityShort
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myShort")));

            script = """
                     Job:TestJob()
                     {
                         Short(23);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityShort
            {
                Value = (WitConstantNumeric)"23"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Short:myShort1 = 23;
                             Short:myShort2 = myShort1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myShort1"], Was.EqualTo(new WitVariableShort("myShort1")));
            Assert.That(job.Variables["myShort2"], Was.EqualTo(new WitVariableShort("myShort2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityShort
            {
                Value = (WitConstantNumeric)"23"
            }.WithReturnReference("myShort1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityShort
            {
                Value = (WitReference)"myShort1"
            }.WithReturnReference("myShort2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Short:myShort = Short();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityShort>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Short:myShort = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityShort>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myShort = 23;
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
                             Short:myShort = Short(23);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myShort"].Value, Is.EqualTo((short)23));

            script = """
                     Job:TestJob()
                     {
                         Short:myShort = 23;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myShort"].Value, Is.EqualTo((short)23));
            
            script = """
                     Job:TestJob()
                     {
                         Short:myShort1 = 23;
                         Short:myShort2 = myShort1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myShort1"].Value, Is.EqualTo((short)23));
            Assert.That(job.Variables["myShort2"].Value, Is.EqualTo((short)23));
        }
        
    }
}
