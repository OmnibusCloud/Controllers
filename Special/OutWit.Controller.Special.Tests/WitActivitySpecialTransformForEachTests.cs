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
    public class WitActivitySpecialTransformForEachTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialTransformForEach
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
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"10",
                (WitConstantNumeric)"20",
                (WitConstantNumeric)"30"
            }));
            Assert.That(activity.ReturnReference, Was.EqualTo(null));
            Assert.That(activity.Transformer, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Transform.ForEach(obj in [10, 20, 30]);
                                                        """));

            activity = new WitActivitySpecialTransformForEach
            {
                IterationVariable = (WitReference)"obj",
                Keyword = (WitCondition)"in",
                Collection = (WitReference)"collection"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(activity.ReturnReference, Was.EqualTo(null));
            Assert.That(activity.Transformer, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Transform.ForEach(obj in collection);
                                                        """));

            activity.SetTransformer(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            });

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(activity.ReturnReference, Was.EqualTo(null));
            Assert.That(activity.Transformer, Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            }));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Transform.ForEach(obj in collection) => Trace(obj);
                                                        """));

            activity.SetReturnReference("result");

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(activity.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(activity.ReturnReference, Was.EqualTo("result"));
            Assert.That(activity.Transformer, Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            }));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        result = Transform.ForEach(obj in collection) => Trace(obj);
                                                        """));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialTransformForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
                }
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                });

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(forEach => forEach.StagesCount, 20)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(forEach => forEach.IterationVariable, (WitReference)"obj1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(forEach => forEach.Keyword, (WitCondition)"or")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(forEach => forEach.Collection, (WitConstantNumeric)"collection1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(forEach => forEach.ReturnReference, "result1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(forEach => forEach.Transformer, new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj1"
            })));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialTransformForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
                }
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                });

            var clone = activity.Clone() as WitActivitySpecialTransformForEach;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(clone.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(clone.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(clone.ReturnReference, Was.EqualTo("result"));
            Assert.That(clone.Transformer, Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            }));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialTransformForEach
                {
                    IterationVariable = (WitReference)"obj",
                    Keyword = (WitCondition)"in",
                    Collection = (WitReference)"collection"
                }
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialTransformForEach;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.IterationVariable, Was.EqualTo((WitReference)"obj"));
            Assert.That(clone.Keyword, Was.EqualTo((WitCondition)"in"));
            Assert.That(clone.Collection, Was.EqualTo((WitReference)"collection"));
            Assert.That(clone.ReturnReference, Was.EqualTo("result"));
            Assert.That(clone.Transformer, Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            }));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Transform.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTransformForEach
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
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                })));

            script = """
                         Job:TestJob()
                         {
                             Object:result = Transform.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableObject("result")));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTransformForEach
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
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                })));

            script = """
                         Job:TestJob()
                         {
                             result = Transform.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialTransformForEach
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
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                })));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Transform.ForEach(obj in collection);
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<IWitActivity>>());

            script = """
                     Job:TestJob()
                     {
                        Transform.ForEach(obj > collection) => Trace(obj);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialTransformForEach>>());
        }

        [Test]
        public void ForEachTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             
                             Transform.ForEach(obj in [10, 20, 30]) => Trace(obj);
                             
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
            Assert.That(messages[1], Is.EqualTo("10"));
            Assert.That(messages[2], Is.EqualTo("20"));
            Assert.That(messages[3], Is.EqualTo("30"));
            Assert.That(messages[4], Is.EqualTo("after invoke"));
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
                             
                             Transform.ForEach(obj in collection) => Trace(obj.Item1);
                             Transform.ForEach(obj in collection) => Trace(obj.Item2);
                             
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
            Assert.That(progress.Count, Is.EqualTo(7));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("1"));
            Assert.That(messages[2], Is.EqualTo("2"));
            Assert.That(messages[3], Is.EqualTo("3"));
            Assert.That(messages[4], Is.EqualTo("4"));
            Assert.That(messages[5], Is.EqualTo("5"));
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
                         
                            Transform.ForEach(obj in [10, 20, 30]) => Trace(obj, true);
                         
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
            Assert.That(messages[1], Is.EqualTo("10"));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }

        [Test]
        public void ForEachWithReturnTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Trace("before invoke");
                                                  
                            ObjectCollection:collection = Transform.ForEach(obj in [10, 20, 30]) => Int(obj);
                                                  
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
            Assert.That(messages.Count, Is.EqualTo(2));
            Assert.That(progress.Count, Is.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("before invoke"));
            Assert.That(messages[1], Is.EqualTo("after invoke"));
            Assert.That(job.Variables["collection"].Value, Is.EqualTo(new object[] {10, 20, 30}));
        }
    }
}