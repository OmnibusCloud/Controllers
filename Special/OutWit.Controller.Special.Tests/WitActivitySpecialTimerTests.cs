using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialTimerTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialTimer
            {
                Interval = (WitConstantNumeric)"10",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.Interval, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Timeout, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Timer(10)
                                                        {
                                                        }
                                                        """));

            activity = new WitActivitySpecialTimer
            {
                Interval = (WitConstantNumeric)"10",
                Timeout = (WitConstantNumeric)"20"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.Interval, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Timeout, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Timer(10, 20)
                                                        {
                                                        }
                                                        """));

            activity.AddVariable(new WitVariableObject("obj"));
            activity.AddActivity(new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"test1"
            });
            activity.AddActivity(new WitActivitySpecialTrace
            {
                Message = (WitConstantString)"test2"
            });

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Activities.Count, Is.EqualTo(2));
            Assert.That(activity.Variables.Count, Is.EqualTo(1));
            Assert.That(activity.Interval, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Timeout, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Timer(10, 20)
                                                        {
                                                            Object:obj;
                                                        
                                                            Trace("test1");
                                                            Trace("test2");
                                                        }
                                                        """));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialTimer
                {
                    Interval = (WitConstantNumeric)"10",
                    Timeout = (WitConstantNumeric)"20"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 20)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Interval, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Timeout, (WitConstantNumeric)"21")));
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialTimer
                {
                    Interval = (WitConstantNumeric)"10",
                    Timeout = (WitConstantNumeric)"20"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test3"
                })));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialTimer
                {
                    Interval = (WitConstantNumeric)"10",
                    Timeout = (WitConstantNumeric)"20"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.Clone() as WitActivitySpecialTimer;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(activity.Interval, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Timeout, Was.EqualTo((WitConstantNumeric)"20"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialTimer
                {
                    Interval = (WitConstantNumeric)"10",
                    Timeout = (WitConstantNumeric)"20"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialTimer;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(activity.Interval, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Timeout, Was.EqualTo((WitConstantNumeric)"20"));
        }


        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                        Timer(10)
                        {
                            Object:obj;
                     
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTimer
                {
                    Interval = (WitConstantNumeric)"10",
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                })));

            script = """
                     Job:TestJob()
                     {
                        Timer(10, 20)
                        {
                            Object:obj;
                     
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTimer
                {
                    Interval = (WitConstantNumeric)"10",
                    Timeout = (WitConstantNumeric)"20"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                })));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                     Job:TestJob()
                     {
                        Timer()
                        {
                            Object:obj;
                                          
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialTimer>>());

            script = """
                     Job:TestJob()
                     {
                        Timer("10")
                        {
                            Object:obj;
                                          
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialTimer>>());
        }

        [Test]
        public void TimerTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before timer");
                             Timer(50, 300)
                             {
                                Trace("tick");
                             }
                             Trace("after timer");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<string?>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}: {DateTime.Now.Millisecond:0}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(task.Status?.Duration?.TotalMilliseconds, Is.GreaterThan(300));
            Assert.That(task.Status?.Duration?.TotalMilliseconds, Is.LessThan(400));
            Assert.That(messages.Count, Is.EqualTo(8));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(progress.Last(), Is.EqualTo(100));
            Assert.That(messages[0], Is.EqualTo("before timer"));
            Assert.That(messages[1], Is.EqualTo("tick"));
            Assert.That(messages[2], Is.EqualTo("tick"));
            Assert.That(messages[3], Is.EqualTo("tick"));
            Assert.That(messages[4], Is.EqualTo("tick"));
            Assert.That(messages[5], Is.EqualTo("tick"));
            Assert.That(messages[6], Is.EqualTo("tick"));
            Assert.That(messages[7], Is.EqualTo("after timer"));
        }

        [Test]
        public void TimerNestedTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before timer");
                             Timer(50, 100)
                             {
                                Trace("tick");
                                Invoke()
                                {
                                    Trace("tac");
                                }
                             }
                             Trace("after timer");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<string?>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}: {DateTime.Now.Millisecond:0}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(task.Status?.Duration?.TotalMilliseconds, Is.GreaterThan(100));
            Assert.That(task.Status?.Duration?.TotalMilliseconds, Is.LessThan(200));
            Assert.That(messages.Count, Is.EqualTo(6));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(progress.Last(), Is.EqualTo(100));
            Assert.That(messages[0], Is.EqualTo("before timer"));
            Assert.That(messages[1], Is.EqualTo("tick"));
            Assert.That(messages[2], Is.EqualTo("tac"));
            Assert.That(messages[3], Is.EqualTo("tick"));
            Assert.That(messages[4], Is.EqualTo("tac"));
            Assert.That(messages[5], Is.EqualTo("after timer"));
        }
    }
}
