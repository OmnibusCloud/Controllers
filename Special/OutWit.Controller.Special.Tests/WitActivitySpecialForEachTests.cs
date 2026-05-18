using System;
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
    public class WitActivitySpecialForEachTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialForEach
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
                                                        ForEach(obj in [10, 20, 30])
                                                        {
                                                        }
                                                        """));

            activity = new WitActivitySpecialForEach
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
                                                        ForEach(obj in collection)
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
                                                        ForEach(obj in collection)
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
            var activity = new WitActivitySpecialForEach
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
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialForEach
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
            var activity = new WitActivitySpecialForEach
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

            var clone = activity.Clone() as WitActivitySpecialForEach;

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
            var activity = new WitActivitySpecialForEach
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

            var clone = activity.MemoryPackClone() as WitActivitySpecialForEach;

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
                             ForEach(obj in [10, 20, 30])
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialForEach
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
                         ForEach(obj in collection)
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialForEach
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
                            ForEach(obj)
                            {
                                Object:obj;
                                              
                                Trace("test1");
                                Trace("test2");
                            }
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialForEach>>());

            script = """
                     Job:TestJob()
                     {
                        ForEach(obj > collection)
                        {
                            Object:obj;
                                          
                            Trace("test1");
                            Trace("test2");
                        }
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialForEach>>());
        }

        [Test]
        public void ForEachTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             String:result;
                             Trace("before invoke");
                             ForEach(str in ["invoke1", "invoke2", "invoke3"])
                             {
                                Trace(str);
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
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(messages[2], Is.EqualTo("invoke2"));
            Assert.That(messages[3], Is.EqualTo("invoke3"));
            Assert.That(messages[4], Is.EqualTo("after invoke"));
            Assert.That(job.Variables["result"].Value, Is.EqualTo("invoke3"));
        }
        
               [Test]
        public void ForEachWithTuppleTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Array:array1 = [1, 2, 3];
                             Array:array2 = [4, 5, 6];
                         
                             TupleCollection:collection = Zip(array1, array2);
                             
                             Trace("before invoke");
                             ForEach(obj in collection)
                             {
                                Trace(obj.Item1);
                                Trace(obj.Item2);
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
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(8));
            Assert.That(progress.Count, Is.EqualTo(6));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("1"));
            Assert.That(messages[2], Is.EqualTo("4"));
            Assert.That(messages[3], Is.EqualTo("2"));
            Assert.That(messages[4], Is.EqualTo("5"));
            Assert.That(messages[5], Is.EqualTo("3"));
            Assert.That(messages[6], Is.EqualTo("6"));
            Assert.That(messages[7], Is.EqualTo("after invoke"));
        }

        [Test]
        public void ForEachWithFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(str in ["invoke1", "invoke2", "invoke3"])
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
            Assert.That(messages.Count, Is.EqualTo(2));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke1"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ForEachNestedTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             String:result1;
                             String:result2;
                             Trace("before invoke");
                             ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
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
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke11"));
            Assert.That(messages[2], Is.EqualTo("invoke21"));
            Assert.That(messages[3], Is.EqualTo("invoke22"));
            Assert.That(messages[4], Is.EqualTo("invoke23"));
            Assert.That(messages[5], Is.EqualTo("invoke12"));
            Assert.That(messages[6], Is.EqualTo("invoke21"));
            Assert.That(messages[7], Is.EqualTo("invoke22"));
            Assert.That(messages[8], Is.EqualTo("invoke23"));
            Assert.That(messages[9], Is.EqualTo("after invoke"));
            Assert.That(job.Variables["result1"].Value, Is.EqualTo("invoke12"));
            Assert.That(job.Variables["result2"].Value, Is.EqualTo("invoke23"));
        }

        [Test]
        public void ForEachNestedWithFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
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
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke11"));
            Assert.That(messages[2], Is.EqualTo("invoke21"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ForEachWithCancelTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
                                {
                                    Trace(str2);
                                    Pause(1000);
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

            Assert.That(task.Status?.Duration, Is.LessThan(TimeSpan.FromMilliseconds(500)));
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("invoke11"));
            Assert.That(messages[2], Is.EqualTo("invoke21"));
        }

        [Test]
        public void ForEachWithPauseTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             ForEach(str1 in ["invoke11", "invoke12"])
                             {
                                Trace(str1);
                                ForEach(str2 in ["invoke21", "invoke22", "invoke23"])
                                {
                                    Trace(str2);
                                    Pause(50);
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
            Assert.That(messages.Count, Is.EqualTo(10));
            Assert.That(messages[0].Item2, Is.EqualTo("before invoke"));
            Assert.That(messages[1].Item2, Is.EqualTo("invoke11"));
            Assert.That(messages[2].Item2, Is.EqualTo("invoke21"));
            Assert.That(messages[3].Item2, Is.EqualTo("invoke22"));
            Assert.That(messages[4].Item2, Is.EqualTo("invoke23"));
            Assert.That(messages[5].Item2, Is.EqualTo("invoke12"));
            Assert.That(messages[6].Item2, Is.EqualTo("invoke21"));
            Assert.That(messages[7].Item2, Is.EqualTo("invoke22"));
            Assert.That(messages[8].Item2, Is.EqualTo("invoke23"));
            Assert.That(messages[9].Item2, Is.EqualTo("after invoke"));
        }
    }
}