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
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialDelayedTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialDelayed
            {
                Delay = (WitConstantNumeric)"10",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.Delay, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Delay(10)
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

            Assert.That(activity.StagesCount, Is.EqualTo(2));
            Assert.That(activity.Activities.Count, Is.EqualTo(2));
            Assert.That(activity.Variables.Count, Is.EqualTo(1));
            Assert.That(activity.Delay, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Delay(10)
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
            var activity = new WitActivitySpecialDelayed
                {
                    Delay = (WitConstantNumeric)"10",
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
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Delay, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialDelayed
                {
                    Delay = (WitConstantNumeric)"10",
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
            var activity = new WitActivitySpecialDelayed
                {
                    Delay = (WitConstantNumeric)"10",
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.Clone() as WitActivitySpecialDelayed;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.Delay, Was.EqualTo((WitConstantNumeric)"10"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialDelayed
                {
                    Delay = (WitConstantNumeric)"10",
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialDelayed;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.Delay, Was.EqualTo((WitConstantNumeric)"10"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                        Delay(10)
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialDelayed
                {
                    Delay = (WitConstantNumeric)"10",
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
                        Delay()
                        {
                            Object:obj = NULL;
                                          
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialDelayed>>());
        }
        
        [Test]
        public void DelayTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Delay(500)
                             {
                                Trace("invoke1");
                                Trace("invoke2");
                                Trace("invoke3");
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
            Assert.That(task.Status?.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(500)));
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(600)));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(progress.Count, Is.EqualTo(5));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke3"));
            Assert.That(messages[4], Is.EqualTo("after invoke"));
        }

        [Test]
        public void DelayWithFailTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Delay(500)
                         {
                            Trace("invoke1");
                            Trace("invoke2", true);
                            Trace("invoke3");
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<string>();
            var progress = new List<double>();

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

            Assert.That(task.Status?.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(500)));
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(600)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(progress.Count, Is.EqualTo(2));
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void DelayNestedTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Delay(500)
                         {
                            Trace("invoke1");
                            Delay(200)
                            {
                                Trace("invoke2");
                                Trace("invoke3");
                            }
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<string>();
            var progress = new List<double>();

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

            Assert.That(task.Status?.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(700)));
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(800)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.Count, Is.EqualTo(5));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke3"));
            Assert.That(messages[4], Is.EqualTo("after invoke"));
        }

        [Test]
        public void DelayNestedWithFailTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Delay(500)
                         {
                            Trace("invoke1");
                            Delay(200)
                            {
                                Trace("invoke2", true);
                                Trace("invoke3");
                            }
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<string>();
            var progress = new List<double>();

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

            Assert.That(task.Status?.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(700)));
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(800)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(progress.Count, Is.EqualTo(2));
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void DelayWithCancelTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Invoke()
                         {
                            Trace("invoke1");
                            Delay(1000)
                            {
                                Trace("invoke2");
                                Trace("invoke3");
                            }
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<string>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine($"{p:0.00}");
            };
            task.Run();

            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(100);
                task.Cancel();
            });

            resetEvent.WaitOne();

            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(800)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(progress.Count, Is.EqualTo(2));
            Assert.That(messages.Count, Is.EqualTo(2));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
        }

        [Test]
        public void DelayWithPauseTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Invoke()
                         {
                            Trace("invoke1");
                            Delay(50)
                            {
                                Trace("invoke2");
                                Trace("invoke3");
                            }
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new List<(DateTime, string)>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add((DateTime.Now, mes));
                Console.Write($"{mes}: ");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
                Console.WriteLine($"{p:0.00}");
            };
            task.Run();

            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(10);
                task.Pause();
            });

            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(500);
                task.Resume();
            });

            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.Count, Is.EqualTo(5));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(messages[4].Item1 - messages[0].Item1, Is.GreaterThan(TimeSpan.FromMilliseconds(450)));
            Assert.That(messages[0].Item2, Is.EqualTo("before invoke"));
            Assert.That(messages[1].Item2, Is.EqualTo("invoke1"));
            Assert.That(messages[2].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[3].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[4].Item2, Is.EqualTo("after invoke"));
        }
    }
}
