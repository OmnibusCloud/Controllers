using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.Conditions;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    [Explicit]
    public class WitActivitySpecialParallelForEachTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialParallelForEach
            {
                IterationVariable = (WitReference)"obj",
                Keyword = (WitCondition)"in",
                Collection = (WitArray)new IWitParameter[]
                {
                    (WitConstantNumeric)"10",
                    (WitConstantNumeric)"20",
                    (WitConstantNumeric)"30"
                }
            };
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"10",
                (WitConstantNumeric)"20",
                (WitConstantNumeric)"30"
            }));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Parallel.ForEach(obj in [10, 20, 30])
                                                        {
                                                        }
                                                        """));

            activity = new WitActivitySpecialParallelForEach
            {
                IterationVariable = (WitReference)"obj",
                Keyword = (WitCondition)"in",
                Collection = (WitReference)"collection"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Parallel.ForEach(obj in collection)
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
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Parallel.ForEach(obj in collection)
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
            var activity = new WitActivitySpecialParallelForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
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
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.IterationVariable, (WitReference)"obj1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Keyword, (WitCondition)"or")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Collection, (WitConstantNumeric)"collection1")));
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialParallelForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
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
            var activity = new WitActivitySpecialParallelForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.Clone() as WitActivitySpecialParallelForEach;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(clone.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(clone.Collection, Was.EqualTo((WitReference)"collection"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialParallelForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialParallelForEach;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(clone.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(clone.Collection, Was.EqualTo((WitReference)"collection"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Parallel.ForEach(obj in [10, 20, 30])
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialParallelForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitArray)new IWitParameter[]
                    {
                        (WitConstantNumeric)"10",
                        (WitConstantNumeric)"20",
                        (WitConstantNumeric)"30"
                    }
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
                         Parallel.ForEach(obj in collection)
                         {
                            Trace("test1");
                            Trace("test2");
                         }
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialParallelForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
                }
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                })
                .WithActivity(new WitActivitySpecialTrace
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
                            Parallel.ForEach(obj)
                            {
                                Object:obj;
                                              
                                Trace("test1");
                                Trace("test2");
                            }
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialParallelForEach>>());

            script = """
                     Job:TestJob()
                     {
                        Parallel.ForEach(obj > collection)
                        {
                            Object:obj;
                                          
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialParallelForEach>>());
        }
        
        [Test]
        public void ParallelForEachTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             String:result;
                             Trace("before invoke");
                             Parallel.ForEach(str in ["invoke1", "invoke2", "invoke3"])
                             {
                                Trace(str);
                                Pause(100);
                                result = str;
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

            task.ProcessingStarted += (_) => { started = true; };
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
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(200)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(messages, Contains.Item("after invoke"));
            Assert.That(job.Variables["result"].Value, Is.Not.Empty);
            Assert.That(job.Variables["result"].Value, Is.Not.Null);
        }

        [Test]
        public void ParallelForEachWithFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Parallel.ForEach(str in ["invoke1", "invoke2", "invoke3"])
                             {
                                Trace(str, true);
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
            Assert.That(messages.Count, Is.EqualTo(4));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke1"));
            Assert.That(messages, Contains.Item("invoke2"));
            Assert.That(messages, Contains.Item("invoke3"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ParallelForEachNestedTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             String:result1;
                             String:result2;
                             Trace("before invoke");
                             Parallel.ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                Parallel.ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
                                {
                                    Trace(str2);
                                    result1 = str1;
                                    result2 = str2;
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
            Assert.That(messages.Count, Is.EqualTo(10));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke11"));
            Assert.That(messages, Contains.Item("invoke21"));
            Assert.That(messages, Contains.Item("invoke22"));
            Assert.That(messages, Contains.Item("invoke23"));
            Assert.That(messages, Contains.Item("invoke12"));
            Assert.That(messages, Contains.Item("invoke21"));
            Assert.That(messages, Contains.Item("invoke22"));
            Assert.That(messages, Contains.Item("invoke23"));
            Assert.That(messages, Contains.Item("after invoke"));
            Assert.That(job.Variables["result1"].Value, Is.Not.Empty);
            Assert.That(job.Variables["result1"].Value, Is.Not.Null);
            Assert.That(job.Variables["result2"].Value, Is.Not.Empty);
            Assert.That(job.Variables["result2"].Value, Is.Not.Null);
        }

        [Test]
        public void ParallelForEachNestedWithFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                Parallel.ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
                                {
                                    Trace(str2, true);
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
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke11"));
            Assert.That(messages, Contains.Item("invoke21"));
            Assert.That(messages, Contains.Item("invoke22"));
            Assert.That(messages, Contains.Item("invoke23"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ParallelForEachWithCancelTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Parallel.ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                Parallel.ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
                                {
                                    Pause(1000);
                                    Trace(str2);
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

            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(500)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages, Contains.Item("before invoke"));
            Assert.That(messages, Contains.Item("invoke11"));
            Assert.That(messages, Contains.Item("invoke12"));
        }

        [Test]
        public void ParallelForEachWithPauseTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             Parallel.ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                Parallel.ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
                                {
                                    Pause(50);
                                    Trace(str2);
                                }
                             }
                             Trace("after invoke");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var messages = new ConcurrentBag<(DateTime, string)>();
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

            Assert.That(task.Status?.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(500)));
            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(600)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages.Count, Is.EqualTo(10));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("before invoke"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke11"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke21"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke22"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke23"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke12"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke21"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke22"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("invoke23"));
            Assert.That(messages.Select(x=>x.Item2), Contains.Item("after invoke"));
        }

    }
}