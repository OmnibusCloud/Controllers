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
    public class WitActivityBooleanTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityBoolean();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Bool();"));

            activity = new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantBoolean)"true"));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Bool(true);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantBoolean)"true"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Bool(true);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantBoolean)"false")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityBoolean;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantBoolean)"true"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityBoolean;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo((WitConstantBoolean)"true"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Bool:myBool = Bool(true);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            
            var hh = new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("myBool");


            var gg = job.Activities.Single() as WitActivityBoolean;

            var jj = gg.Is(hh);
            
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("myBool")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableBoolean("myBool")));
            
            script = """
                     Job:TestJob()
                     {
                         Bool:myBool = true;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("myBool")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableBoolean("myBool")));

            script = """
                     Job:TestJob()
                     {
                         myBool = Bool(true);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("myBool")));

            script = """
                     Job:TestJob()
                     {
                         Bool(true);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Bool:myBool1 = true;
                             Bool:myBool2 = myBool1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myBool1"], Was.EqualTo(new WitVariableBoolean("myBool1")));
            Assert.That(job.Variables["myBool2"], Was.EqualTo(new WitVariableBoolean("myBool2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityBoolean
            {
                Value = (WitConstantBoolean)"true"
            }.WithReturnReference("myBool1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityBoolean
            {
                Value = (WitReference)"myBool1"
            }.WithReturnReference("myBool2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Bool:myBool = Bool();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityBoolean>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Bool:myBool = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityBoolean>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myBool = true;
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
                             Bool:myBool = Bool(true);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myBool"].Value, Is.EqualTo(true));

            script = """
                     Job:TestJob()
                     {
                         Bool:myBool = true;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myBool"].Value, Is.EqualTo(true));
            
            script = """
                     Job:TestJob()
                     {
                         Bool:myBool1 = true;
                         Bool:myBool2 = myBool1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myBool1"].Value, Is.EqualTo(true));
            Assert.That(job.Variables["myBool2"].Value, Is.EqualTo(true));
        }
        
    }
}
