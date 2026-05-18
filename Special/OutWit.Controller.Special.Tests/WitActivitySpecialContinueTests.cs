using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialContinueTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialContinue();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ToString(), Is.EqualTo("Continue();"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialContinue();

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialContinue();

            var clone = activity.Clone() as WitActivitySpecialContinue;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialContinue();

            var clone = activity.MemoryPackClone() as WitActivitySpecialContinue;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Continue();
                     }
                     """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialContinue()));
            
            script = """
                     Job:TestJob()
                     {
                         Continue;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialContinue()));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Continue(true);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialContinue>>());
        }
        
        [Test]
        public void LoopTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Loop(3)
                             {
                                Trace("invoke1");
                                Continue;
                                Trace("invoke2");
                             }
                             Trace("after invoke");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            bool started = false;
            var messages = new List<string>();
            var progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke1"));
            Assert.That(messages[3], Is.EqualTo("invoke1"));
            Assert.That(messages[4], Is.EqualTo("after invoke"));
        }
        
        [Test]
        public void LoopNestedTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Loop(3)
                             {
                                Trace("invoke1");
                                Loop(2)
                                {
                                    Trace("invoke2");
                                    Continue;
                                    Trace("invoke3");
                                }
                                Trace("invoke4");
                             }
                             Trace("after invoke");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            bool started = false;
            var messages = new List<string>();
            var progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(14));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke2"));
            Assert.That(messages[4], Is.EqualTo("invoke4"));
            Assert.That(messages[5], Is.EqualTo("invoke1"));
            Assert.That(messages[6], Is.EqualTo("invoke2"));
            Assert.That(messages[7], Is.EqualTo("invoke2"));
            Assert.That(messages[8], Is.EqualTo("invoke4"));
            Assert.That(messages[9], Is.EqualTo("invoke1"));
            Assert.That(messages[10], Is.EqualTo("invoke2"));
            Assert.That(messages[11], Is.EqualTo("invoke2"));
            Assert.That(messages[12], Is.EqualTo("invoke4"));
            Assert.That(messages[13], Is.EqualTo("after invoke"));
            
            script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Loop(3)
                             {
                                Trace("invoke1");
                                Loop(2)
                                {
                                    Trace("invoke2");
                                    Continue;
                                    Trace("invoke3");
                                }
                                Continue;
                                Trace("invoke4");
                             }
                             Trace("after invoke");
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            started = false;
            messages = new List<string>();
            progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(11));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke2"));
            Assert.That(messages[4], Is.EqualTo("invoke1"));
            Assert.That(messages[5], Is.EqualTo("invoke2"));
            Assert.That(messages[6], Is.EqualTo("invoke2"));
            Assert.That(messages[7], Is.EqualTo("invoke1"));
            Assert.That(messages[8], Is.EqualTo("invoke2"));
            Assert.That(messages[9], Is.EqualTo("invoke2"));
            Assert.That(messages[10], Is.EqualTo("after invoke"));
        }
        
        [Test]
        public void ForEachTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(index in [1, 2, 3])
                             {
                                Trace(index);
                                Continue;
                                Trace(index);
                             }
                             Trace("after invoke");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            bool started = false;
            var messages = new List<string>();
            var progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("1"));
            Assert.That(messages[2], Is.EqualTo("2"));
            Assert.That(messages[3], Is.EqualTo("3"));
            Assert.That(messages[4], Is.EqualTo("after invoke"));
            
            script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         ForEach(index in [1, 2, 3])
                         {
                            Trace(index);
                            If(index >= 2)
                            {
                                Continue;
                            }
                            Trace(index);
                         }
                         Trace("after invoke");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            started = false;
            messages = new List<string>();
            progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(6));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("1"));
            Assert.That(messages[2], Is.EqualTo("1"));
            Assert.That(messages[3], Is.EqualTo("2"));
            Assert.That(messages[4], Is.EqualTo("3"));
            Assert.That(messages[5], Is.EqualTo("after invoke"));
        }
        
        [Test]
        public void ForEachNestedTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(index1 in [1, 2, 3])
                             {
                                Trace(index1);
                                ForEach(index2 in [1, 2])
                                {
                                    Trace(index2);
                                    Continue;
                                    Trace(index2);
                                }
                             }
                             Trace("after invoke");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            bool started = false;
            var messages = new List<string>();
            var progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(11));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("1"));
            Assert.That(messages[2], Is.EqualTo("1"));
            Assert.That(messages[3], Is.EqualTo("2"));
            Assert.That(messages[4], Is.EqualTo("2"));
            Assert.That(messages[5], Is.EqualTo("1"));
            Assert.That(messages[6], Is.EqualTo("2"));
            Assert.That(messages[7], Is.EqualTo("3"));
            Assert.That(messages[8], Is.EqualTo("1"));
            Assert.That(messages[9], Is.EqualTo("2"));
            Assert.That(messages[10], Is.EqualTo("after invoke"));
            
            script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(index1 in [1, 2, 3])
                             {
                                Trace(index1);
                                ForEach(index2 in [1, 2])
                                {
                                    Trace(index2);
                                    Continue;
                                    Trace(index2);
                                }
                                Continue;
                                Trace(index1);
                             }
                             Trace("after invoke");
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            started = false;
            messages = new List<string>();
            progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true;};
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(11));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("1"));
            Assert.That(messages[2], Is.EqualTo("1"));
            Assert.That(messages[3], Is.EqualTo("2"));
            Assert.That(messages[4], Is.EqualTo("2"));
            Assert.That(messages[5], Is.EqualTo("1"));
            Assert.That(messages[6], Is.EqualTo("2"));
            Assert.That(messages[7], Is.EqualTo("3"));
            Assert.That(messages[8], Is.EqualTo("1"));
            Assert.That(messages[9], Is.EqualTo("2"));
            Assert.That(messages[10], Is.EqualTo("after invoke"));
        }
    }
}
