using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.Activities;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialParallelInvokeTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialParallelInvoke();
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Parallel.Invoke()
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
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Parallel.Invoke()
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
            var activity = new WitActivitySpecialParallelInvoke()
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
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialParallelInvoke()
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
            var activity = new WitActivitySpecialParallelInvoke()
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.Clone() as WitActivitySpecialParallelInvoke;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialParallelInvoke()
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialParallelInvoke;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Parallel.Invoke()
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialParallelInvoke()
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
                         Parallel.Invoke()
                         {
                            Trace("test1");
                            Trace("test2");
                            
                            Parallel.Invoke()
                            {
                                Trace("test3");
                            }
                         }
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialParallelInvoke()
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                })
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                })
                .WithActivity(new WitActivitySpecialParallelInvoke()
                    .WithActivity(new WitActivitySpecialTrace
                    {
                        Message = (WitConstantString)"test3"
                    }))));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                         Job:TestJob()
                         {
                            Parallel.Invoke(3)
                            {
                                Object:obj;
                                              
                                Trace("test1");
                                Trace("test2");
                            }
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialParallelInvoke>>());
        }

        [Test]
        public void ParallelInvokeTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Parallel.Invoke()
                             {
                                Invoke()
                                {
                                    Pause(50);
                                    Trace("invoke1");
                                }
                                Invoke()
                                {
                                    Pause(50);
                                    Trace("invoke2");
                                }
                                Invoke()
                                {
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

            bool started = false;
            var messages = new ConcurrentBag<string?>();
            var progress = new List<double>();

            task.ProcessingStarted += (_) => { started = true; };
            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Console.WriteLine(string.Join(", ", messages));

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(task.Status?.Duration?.TotalMilliseconds, Is.LessThan(150));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(progress.OrderBy(x=>x), Is.EqualTo(progress));
            Assert.That(progress.Last(), Is.EqualTo(100));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(messages, Contains.Item("after invoke"));
        }

        [Test]
        public void ParallelInvokeWithFailTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Parallel.Invoke()
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
                Console.WriteLine($"{mes}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(progress.OrderBy(x=>x), Is.EqualTo(progress));
            Assert.That(progress.Last(), Is.LessThan(100));
            Assert.That(messages.Count, Is.EqualTo(4));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ParallelInvokeNestedTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Parallel.Invoke()
                         {
                            Trace("invoke1");
                            Parallel.Invoke()
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

            var messages = new ConcurrentBag<string>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.OrderBy(x=>x), Is.EqualTo(progress));
            Assert.That(progress.Last(), Is.EqualTo(100));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(messages, Contains.Item("after invoke"));
        }

        [Test]
        public void ParallelInvokeNestedWithFailTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Parallel.Invoke()
                         {
                            Trace("invoke1");
                            Parallel.Invoke()
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

            var messages = new ConcurrentBag<string>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(progress.OrderBy(x => x), Is.EqualTo(progress));
            Assert.That(progress.Last(), Is.LessThan(100));
            Assert.That(messages.Count, Is.EqualTo(4));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ParallelInvokeWithCancelTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Parallel.Invoke()
                         {
                            Trace("invoke1");
                            Parallel.Invoke()
                            {
                                Trace("invoke2");
                                Invoke()
                                {
                                    Pause(1000);
                                    Trace("invoke3");
                                }
                            }
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new ConcurrentBag<string>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run();

            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(100);
                task.Cancel();
            });

            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(progress.OrderBy(x => x), Is.EqualTo(progress));
            Assert.That(progress.Last(), Is.LessThan(100));
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
        }

        [Test]
        public void ParallelInvokeWithPauseTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Trace("before invoke");
                         Parallel.Invoke()
                         {
                            Trace("invoke1");
                            Parallel.Invoke()
                            {
                                Trace("invoke2");
                                Invoke()
                                {
                                    Pause(50);
                                    Trace("invoke3");
                                }
                            }
                         }
                         Trace("after invoke");
                     }
                     """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new ConcurrentBag<string>();
            var progress = new List<double>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Trace += (_, mes) =>
            {
                messages.Add(mes);
                Console.WriteLine($"{mes}");
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
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

            Assert.That(task.Status?.Duration?.TotalMilliseconds, Is.GreaterThan(450));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.OrderBy(x => x), Is.EqualTo(progress));
            Assert.That(progress.Last(), Is.EqualTo(100));
            Assert.That(messages.Count, Is.EqualTo(5));
            
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(messages, Contains.Item("after invoke"));
        }

    }
}