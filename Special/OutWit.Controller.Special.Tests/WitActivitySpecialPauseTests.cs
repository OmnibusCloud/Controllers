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
using Microsoft.Build.Utilities;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialPauseTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Message, Was.EqualTo(null));
            Assert.That(activity.Timeout, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("Pause(10);"));

            activity = new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10",
                Message = (WitConstantString)"text",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(activity.Timeout, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("Pause(10, \"text\");"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10",
                Message = (WitConstantString)"text",
            };

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Message, (WitConstantString)"text1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Timeout, (WitConstantNumeric)"11")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10",
                Message = (WitConstantString)"text"
            };

            var clone = activity.Clone() as WitActivitySpecialPause;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(clone.Timeout, Was.EqualTo((WitConstantNumeric)"10"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10",
                Message = (WitConstantString)"text"
            };

            var clone = activity.MemoryPackClone() as WitActivitySpecialPause;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Message, Was.EqualTo((WitConstantString)"text"));
            Assert.That(clone.Timeout, Was.EqualTo((WitConstantNumeric)"10"));
        }
        
        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Pause(10);
                     }
                     """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10"
            }));

            script = """
                     Job:TestJob()
                     {
                         Pause(10, "text");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialPause
            {
                Timeout = (WitConstantNumeric)"10",
                Message = (WitConstantString)"text"
            }));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Pause();
                     }
                     """;
            
            Assert.That(()=>WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialPause>>());

            script = """
                     Job:TestJob()
                     {
                         Pause("text");
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialPause>>());


            script = """
                     Job:TestJob()
                     {
                         Pause("text", "10");
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialPause>>());
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before pause");
                             Pause(500);
                             Trace("after pause");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<(DateTime, string)>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add((DateTime.Now, mes));
                Console.WriteLine(mes);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages[0].Item2, Is.EqualTo("before pause"));
            Assert.That(messages[1].Item2, Is.EqualTo("after pause"));
            Assert.That(messages[1].Item1 - messages[0].Item1, 
                Is.EqualTo(TimeSpan.FromSeconds(0.5)).Within(TimeSpan.FromMilliseconds(20)));

            script = """
                     Job:TestJob()
                     {
                        Trace("before pause");
                        Pause(500, "pause started");
                        Trace("after pause");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            messages = new List<(DateTime, string)>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add((DateTime.Now, mes));
                Console.WriteLine(mes);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages[0].Item2, Is.EqualTo("before pause"));
            Assert.That(messages[1].Item2, Is.EqualTo("pause started"));
            Assert.That(messages[2].Item2, Is.EqualTo("after pause"));
            Assert.That(messages[2].Item1 - messages[0].Item1,
                Is.EqualTo(TimeSpan.FromSeconds(0.5)).Within(TimeSpan.FromMilliseconds(20)));


            script = """
                     Job:TestJob()
                     {
                        Trace("before pause");
                        Pause(5000);
                        Trace("after pause");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            messages = new List<(DateTime, string)>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add((DateTime.Now, mes));
                Console.WriteLine(mes);
            };
            task.Run();
            
            System.Threading.Tasks.Task.Run(()=>
            {
                Thread.Sleep(100);
                task.Cancel();
            });
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(messages.Single().Item2, Is.EqualTo("before pause"));
            
        }
    }
}
