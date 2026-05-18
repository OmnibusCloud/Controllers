using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialTraceTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(activity.ThrowException, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Trace(\"text\");"));

            activity = new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text",
                ThrowException = (WitConstantBoolean)"true"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(activity.ThrowException, Was.EqualTo((WitConstantBoolean)"true"));
            Assert.That(activity.ToString(), Is.EqualTo("Trace(\"text\", true);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text",
                ThrowException = (WitConstantBoolean)"true"
            };

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Message, (WitConstantString)"text1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ThrowException, (WitConstantBoolean)"false")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text",
                ThrowException = (WitConstantBoolean)"true"
            };

            var clone = activity.Clone() as WitActivitySpecialTrace;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(clone.ThrowException, Was.EqualTo((WitConstantBoolean)"true"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text",
                ThrowException = (WitConstantBoolean)"true"
            };

            var clone = activity.MemoryPackClone() as WitActivitySpecialTrace;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(clone.ThrowException, Was.EqualTo((WitConstantBoolean)"true"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("text");
                     }
                     """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text"
            }));

            script = """
                     Job:TestJob()
                     {
                         Trace(21);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitConstantNumeric)"21"
            }));

            script = """
                     Job:TestJob()
                     {
                         Trace("text", true);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"text",
                ThrowException = (WitConstantBoolean)"true"
            }));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace();
                     }
                     """;
            
            Assert.That(()=>WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialTrace>>());


            script = """
                     Job:TestJob()
                     {
                         Trace("text", "true");
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialTrace>>());
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("text");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);
            
            var messages = new List<string>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine(mes);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Single(), Is.EqualTo("text"));

            script = """
                     Job:TestJob()
                     {
                         Trace(21);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            messages = new List<string>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine(mes);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Single(), Is.EqualTo("21"));


            script = """
                     Job:TestJob()
                     {
                        String:str = "text";
                        Trace(str);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            messages = new List<string>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine(mes);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Single(), Is.EqualTo("text"));

            script = """
                     Job:TestJob()
                     {
                        Trace("text", true);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            messages = new List<string>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine(mes);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(messages.Single(), Is.EqualTo("text"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace(str, true);
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
