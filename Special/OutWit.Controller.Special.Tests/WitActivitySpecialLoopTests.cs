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
    public class WitActivitySpecialLoopTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialLoop
            {
                IterationsCount = (WitConstantNumeric)"10",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.IterationsCount, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Loop(10)
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
            Assert.That(activity.IterationsCount, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Loop(10)
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
            var activity = new WitActivitySpecialLoop
                {
                    IterationsCount = (WitConstantNumeric)"10",
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
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.IterationsCount, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialLoop
                {
                    IterationsCount = (WitConstantNumeric)"10",
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
            var activity = new WitActivitySpecialLoop
                {
                    IterationsCount = (WitConstantNumeric)"10",
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.Clone() as WitActivitySpecialLoop;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.IterationsCount, Was.EqualTo((WitConstantNumeric)"10"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialLoop
                {
                    IterationsCount = (WitConstantNumeric)"10",
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialLoop;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.IterationsCount, Was.EqualTo((WitConstantNumeric)"10"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                        Loop(10)
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialLoop
                {
                    IterationsCount = (WitConstantNumeric)"10",
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
                        Loop()
                        {
                            Object:obj;
                                          
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialLoop>>());
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
            Assert.That(messages.Count, Is.EqualTo(8));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke1"));
            Assert.That(messages[4], Is.EqualTo("invoke2"));
            Assert.That(messages[5], Is.EqualTo("invoke1"));
            Assert.That(messages[6], Is.EqualTo("invoke2"));
            Assert.That(messages[7], Is.EqualTo("after invoke"));
        }

        [Test]
        public void LoopWithFailTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Loop(3)
                         {
                            Trace("invoke1");
                            Trace("invoke2", true);
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
        public void LoopNestedTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Loop(2)
                         {
                            Trace("invoke1");
                            Loop(3)
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

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages.Count, Is.EqualTo(16));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke3"));
            Assert.That(messages[4], Is.EqualTo("invoke2"));
            Assert.That(messages[5], Is.EqualTo("invoke3"));
            Assert.That(messages[6], Is.EqualTo("invoke2"));
            Assert.That(messages[7], Is.EqualTo("invoke3"));
            Assert.That(messages[8], Is.EqualTo("invoke1"));
            Assert.That(messages[9], Is.EqualTo("invoke2"));
            Assert.That(messages[10], Is.EqualTo("invoke3"));
            Assert.That(messages[11], Is.EqualTo("invoke2"));
            Assert.That(messages[12], Is.EqualTo("invoke3"));
            Assert.That(messages[13], Is.EqualTo("invoke2"));
            Assert.That(messages[14], Is.EqualTo("invoke3"));
            Assert.That(messages[15], Is.EqualTo("after invoke"));
        }

        [Test]
        public void LoopNestedWithFailTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Loop(2)
                         {
                            Trace("invoke1");
                            Loop(3)
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
        public void LoopWithCancelTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Loop(2)
                         {
                            Trace("invoke1");
                            Loop(3)
                            {
                                Trace("invoke2");
                                Pause(1000);
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

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
        }

        [Test]
        public void LoopWithPauseTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Loop(2)
                         {
                            Trace("invoke1");
                            Loop(3)
                            {
                                Trace("invoke2");
                                Pause(50);
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

            Assert.That(task.Status?.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(750)));
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(900)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages.Count, Is.EqualTo(16));
            Assert.That(messages[0].Item2, Is.EqualTo("before invoke"));
            Assert.That(messages[1].Item2, Is.EqualTo("invoke1"));
            Assert.That(messages[2].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[3].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[4].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[5].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[6].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[7].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[8].Item2, Is.EqualTo("invoke1"));
            Assert.That(messages[9].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[10].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[11].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[12].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[13].Item2, Is.EqualTo("invoke2"));
            Assert.That(messages[14].Item2, Is.EqualTo("invoke3"));
            Assert.That(messages[15].Item2, Is.EqualTo("after invoke"));
        }
    }
}
