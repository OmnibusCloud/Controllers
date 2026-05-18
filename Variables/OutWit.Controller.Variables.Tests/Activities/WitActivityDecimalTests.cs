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
    public class WitActivityDecimalTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityDecimal();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Decimal();"));

            activity = new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Decimal(23.4);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Decimal(23.4);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityDecimal
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
            var activity = new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityDecimal;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantNumeric)"23.4"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityDecimal;

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
                             Decimal:myDecimal = Decimal(23.4);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDecimal")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDecimal("myDecimal")));
            
            script = """
                     Job:TestJob()
                     {
                         Decimal:myDecimal = 23.4;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDecimal")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDecimal("myDecimal")));

            script = """
                     Job:TestJob()
                     {
                         myDecimal = Decimal(23.4);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDecimal")));

            script = """
                     Job:TestJob()
                     {
                         Decimal(23.4);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Decimal:myDecimal1 = 23.4;
                             Decimal:myDecimal2 = myDecimal1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myDecimal1"], Was.EqualTo(new WitVariableDecimal("myDecimal1")));
            Assert.That(job.Variables["myDecimal2"], Was.EqualTo(new WitVariableDecimal("myDecimal2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityDecimal
            {
                Value = (WitConstantNumeric)"23.4"
            }.WithReturnReference("myDecimal1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityDecimal
            {
                Value = (WitReference)"myDecimal1"
            }.WithReturnReference("myDecimal2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Decimal:myDecimal = Decimal();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityDecimal>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Decimal:myDecimal = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDecimal>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myDecimal = 23.4;
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
                             Decimal:myDecimal = Decimal(23.4);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDecimal"].Value, Is.EqualTo((decimal)23.4));

            script = """
                     Job:TestJob()
                     {
                         Decimal:myDecimal = 23.4;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDecimal"].Value, Is.EqualTo((decimal)23.4));
            
            script = """
                     Job:TestJob()
                     {
                         Decimal:myDecimal1 = 23.4;
                         Decimal:myDecimal2 = myDecimal1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDecimal1"].Value, Is.EqualTo((decimal)23.4));
            Assert.That(job.Variables["myDecimal2"].Value, Is.EqualTo((decimal)23.4));
        }
        
    }
}
