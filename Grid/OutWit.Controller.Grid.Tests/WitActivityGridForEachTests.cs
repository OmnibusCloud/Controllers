using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Grid.Activities;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
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

namespace OutWit.Controller.Grid.Tests
{
    [TestFixture]
    public class WitActivitySpecialGridForEachTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var controllersPath = FindControllersPath()
                                  ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false, null, controllersPath);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityGridForEach
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
                                                        Grid.ForEach(obj in [10, 20, 30]);
                                                        """));

            activity = new WitActivityGridForEach
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
                                                        Grid.ForEach(obj in collection);
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
                                                        Grid.ForEach(obj in collection) => Trace(obj);
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
                                                        result = Grid.ForEach(obj in collection) => Trace(obj);
                                                        """));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityGridForEach
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
            var activity = new WitActivityGridForEach
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

            var clone = activity.Clone() as WitActivityGridForEach;

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
                            Grid.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGridForEach
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
                             Object:result = Grid.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableObject("result")));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGridForEach
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
                             result = Grid.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGridForEach
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
                         ProcessingOptions:myOptions = ProcessingOptions("Queued", 2);
                         result = Grid.ForEach(obj in [10, 20, 30], myOptions) => Trace(obj);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityGridForEach
            {
                IterationVariable = (WitReference)"obj",
                Keyword = (WitCondition)"in",
                Collection = (WitArray)new IWitParameter[]
                {
                    (WitConstantNumeric)"10",
                    (WitConstantNumeric)"20",
                    (WitConstantNumeric)"30"
                },
                Options = (WitReference)"myOptions"
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
                            Grid.ForEach(obj in collection);
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<IWitActivity>>());

            script = """
                     Job:TestJob()
                     {
                        Grid.ForEach(obj > collection) => Trace(obj);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivityGridForEach>>());
        }

        [Test]
        public void ForEachUsesTransformerActivityTypeForBenchmarkLookupTest()
        {
            var nodesManager = new MockNodesManager();
            WitEngineSdk.Instance.Reload(Guid.NewGuid(), nodesManager, false);

            var script = """
                         Job:TestJob()
                         {
                             Grid.ForEach(obj in [10, 20, 30]) => Trace(obj);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            using var resetEvent = new AutoResetEvent(false);
            task.ProcessingFinished += (_, __) => resetEvent.Set();
            task.Run();

            Assert.That(resetEvent.WaitOne(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(nodesManager.LastRequestedActivityType, Is.EqualTo(typeof(WitActivitySpecialTrace)));
        }

        [Test]
        public void AllocatorPrefersFasterNodesForEqualWorkloadsTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var slowerNode = new MockActivityNode(node, 10, Guid.NewGuid());
            var fasterNode = new MockActivityNode(node, 20, Guid.NewGuid());

            var tasks = Enumerable.Range(0, 100)
                .Select(_ => new WitGridTask
                {
                    Work = 1,
                    Variables = new WitVariableCollection(),
                    Activity = new WitActivitySpecialTrace { Message = (WitReference)"obj" }
                })
                .ToList();

            var groups = WitGridTaskAllocator.Allocate(new IWitEngineActivityNode[] { slowerNode, fasterNode }, tasks);

            var slowerGroup = groups.Single(group => group.Node.NodeId == slowerNode.NodeId);
            var fasterGroup = groups.Single(group => group.Node.NodeId == fasterNode.NodeId);

            Assert.That(slowerGroup.Count + fasterGroup.Count, Is.EqualTo(100));
            Assert.That(fasterGroup.Count, Is.GreaterThan(slowerGroup.Count));
        }

        [Test]
        [Explicit]
        public void ForEachTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Trace("before invoke");
                             
                             Grid.ForEach(obj in [10, 20, 30]) => Trace(obj);
                             
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
        [Explicit]
        public void ForEachWithFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Trace("before invoke");
                         
                            Grid.ForEach(obj in [10, 20, 30]) => Trace(obj, true);
                         
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
        [Explicit]
        public void ForEachWithReturnTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Trace("before invoke");
                                                  
                            ObjectCollection:collection = Grid.ForEach(obj in [10, 20, 30]) => Int(obj);
                                                  
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
            Assert.That(job.Variables["collection"].Value, Is.EqualTo(new object[] { 10, 20, 30 }));
        }

        // Validates multi-node payload isolation via MockNodesManager.
        // The SDK is single-node by design, so the cross-node send path
        // this test exercises is not reachable through WitEngineSdk —
        // ignored when running against the public SDK package. The full
        // assertion still lives in the closed engine repo's mirror.
        [Test, Ignore("Multi-node path; SDK is single-node only.")]
        public void ForEachDoesNotSendUnrelatedOuterPoolVariablesToNodesTest()
        {
            var manager = new MockNodesManager();
            var controllersPath = FindControllersPath()
                                  ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), manager, false, null, controllersPath);

            try
            {
                var script = """
                             Job:TestJob()
                             {
                                 Object:scene = Object("host-only-scene");
                                 Int:options = Int(42);

                                 Grid.ForEach(item in [10, 20]) => Trace(item);
                             }
                             """;
                var job = WitEngineSdk.Instance.Compile(script);
                var task = WitEngineSdk.Instance.ScheduleProcessing(job);

                var resetEvent = new AutoResetEvent(false);
                task.ProcessingFinished += (_, __) => { resetEvent.Set(); };

                task.Run();
                resetEvent.WaitOne();

                Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
                Assert.That(manager.LastBatchRequests, Is.Not.Null);
                Assert.That(manager.LastBatchRequests, Is.Not.Empty);

                foreach (var request in manager.LastBatchRequests!)
                {
                    var variableNames = request.Pool.Select(me => me.Name).ToArray();

                    Assert.Multiple(() =>
                    {
                        Assert.That(variableNames, Contains.Item("item"));
                        Assert.That(variableNames, Does.Not.Contain("scene"),
                            "Node task pool should not include unrelated outer-scope host variables.");
                        Assert.That(variableNames, Does.Not.Contain("options"),
                            "Node task pool should not include unrelated outer-scope host variables.");
                    });
                }
            }
            finally
            {
                WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false, null, controllersPath);
            }
        }

        private static string? FindControllersPath()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "@Controllers", "Debug");
                if (Directory.Exists(candidate))
                    return candidate;

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}