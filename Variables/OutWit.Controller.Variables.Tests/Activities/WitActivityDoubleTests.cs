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
    public class WitActivityDoubleTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityDouble();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Double();"));

            activity = new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Double(23.4);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Double(23.4);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantNumeric)"11")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityDouble;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityDouble;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Double:myDouble = Double(23.4);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDouble")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDouble("myDouble")));
            
            script = """
                     Job:TestJob()
                     {
                         Double:myDouble = 23.4;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDouble")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDouble("myDouble")));

            script = """
                     Job:TestJob()
                     {
                         myDouble = Double(23.4);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDouble")));

            script = """
                     Job:TestJob()
                     {
                         Double(23.4);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Double:myDouble1 = 23.4;
                             Double:myDouble2 = myDouble1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myDouble1"], Was.EqualTo(new WitVariableDouble("myDouble1")));
            Assert.That(job.Variables["myDouble2"], Was.EqualTo(new WitVariableDouble("myDouble2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityDouble
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDouble1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityDouble
            {
                Value = (WitReference)"myDouble1"
            }.WithReturnReference("myDouble2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Double:myDouble = Double();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityDouble>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Double:myDouble = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDouble>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myDouble = 23.4;
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
                             Double:myDouble = Double(23.4);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDouble"].Value, Is.EqualTo((double)23.4));

            script = """
                     Job:TestJob()
                     {
                         Double:myDouble = 23.4;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDouble"].Value, Is.EqualTo((double)23.4));
            
            script = """
                     Job:TestJob()
                     {
                         Double:myDouble1 = 23.4;
                         Double:myDouble2 = myDouble1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDouble1"].Value, Is.EqualTo((double)23.4));
            Assert.That(job.Variables["myDouble2"].Value, Is.EqualTo((double)23.4));
        }
        
    }
}
