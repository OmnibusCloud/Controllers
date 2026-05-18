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
    public class WitActivityStringTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityString();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("String();"));

            activity = new WitActivityString
            {
                Value = (WitConstantString)"text"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"text"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("String(\"text\");"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"text"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = String(\"text\");"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantString)"text1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityString;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"text"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityString;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"text"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             String:myString = String("text");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("myString")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableString("myString")));
            
            script = """
                     Job:TestJob()
                     {
                         String:myString = "text";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("myString")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableString("myString")));

            script = """
                     Job:TestJob()
                     {
                         myString = String("text");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("myString")));

            script = """
                     Job:TestJob()
                     {
                         String("text");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityString
            {
                Value = (WitConstantString)"text"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             String:myString1 = "text";
                             String:myString2 = myString1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myString1"], Was.EqualTo(new WitVariableString("myString1")));
            Assert.That(job.Variables["myString2"], Was.EqualTo(new WitVariableString("myString2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityString
            {
                Value = (WitConstantString)"text"
            }.WithReturnReference("myString1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityString
            {
                Value = (WitReference)"myString1"
            }.WithReturnReference("myString2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         String:myString = String();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityString>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         String:myString = 23;
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityString>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myString = "text";
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
                             String:myString = String("text");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myString"].Value, Is.EqualTo("text"));

            script = """
                     Job:TestJob()
                     {
                         String:myString = "text";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myString"].Value, Is.EqualTo("text"));
            
            script = """
                     Job:TestJob()
                     {
                         String:myString1 = "text";
                         String:myString2 = myString1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myString1"].Value, Is.EqualTo("text"));
            Assert.That(job.Variables["myString2"].Value, Is.EqualTo("text"));
        }
        
    }
}
