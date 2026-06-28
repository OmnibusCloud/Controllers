using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Grid.Activities;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests
{
    [TestFixture]
    public class WitActivityGridDelegateTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var controllersPath = FindControllersPath()
                                  ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false, null, controllersPath);
        }

        #region Construction / model

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityGridDelegate();

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Options, Was.EqualTo(null));
            Assert.That(activity.ReturnReference, Was.EqualTo(null));
            Assert.That(activity.Transformer, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Grid.Delegate();
                                                        """));

            var withOptions = new WitActivityGridDelegate
            {
                Options = (WitReference)"opt"
            };
            Assert.That(withOptions.Options, Was.EqualTo((WitReference)"opt"));
            Assert.That(withOptions.ToString(), Is.EqualTo("""
                                                           Grid.Delegate(opt);
                                                           """));

            activity.SetTransformer(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            });

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Transformer, Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            }));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        Grid.Delegate() => Trace(obj);
                                                        """));

            activity.SetReturnReference("result");

            Assert.That(activity.ReturnReference, Was.EqualTo("result"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        result = Grid.Delegate() => Trace(obj);
                                                        """));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityGridDelegate
            {
                Options = (WitReference)"opt"
            }
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                });

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(d => d.StagesCount, 20)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(d => d.Options, (WitReference)"opt1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(d => d.ReturnReference, "result1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(d => d.Transformer, new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj1"
            })));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityGridDelegate
            {
                Options = (WitReference)"opt"
            }
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                });

            var clone = activity.Clone() as WitActivityGridDelegate;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone!.StagesCount, Is.EqualTo(1));
            Assert.That(clone.Options, Was.EqualTo((WitReference)"opt"));
            Assert.That(clone.ReturnReference, Was.EqualTo("result"));
            Assert.That(clone.Transformer, Was.EqualTo(new WitActivitySpecialTrace
            {
                Message = (WitReference)"obj"
            }));
        }

        #endregion

        #region Parsing

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Grid.Delegate => Trace(obj);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGridDelegate()
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                })));

            script = """
                     Job:TestJob()
                     {
                         Object:result = Grid.Delegate => Trace(obj);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableObject("result")));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGridDelegate()
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                })));

            script = """
                     Job:TestJob()
                     {
                         result = Grid.Delegate => Trace(obj);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGridDelegate()
                .WithReturnReference("result")
                .WithTransformer(new WitActivitySpecialTrace
                {
                    Message = (WitReference)"obj"
                })));

            script = """
                     Job:TestJob()
                     {
                         ProcessingOptions:myOptions = ProcessingOptions("Queued", 2);
                         result = Grid.Delegate(myOptions) => Trace(obj);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityGridDelegate
            {
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
            // Too many positional parameters (Delegate takes 0 or 1 = options ref).
            var script = """
                         Job:TestJob()
                         {
                            Grid.Delegate(a, b) => Trace(obj);
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivityGridDelegate>>());

            // Options argument must be a reference, not a literal.
            script = """
                     Job:TestJob()
                     {
                        Grid.Delegate(10) => Trace(obj);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivityGridDelegate>>());
        }

        #endregion

        #region Runtime

        [Test]
        public void DelegateUsesTransformerActivityTypeForBenchmarkLookupTest()
        {
            var nodesManager = new MockNodesManager();
            WitEngineSdk.Instance.Reload(Guid.NewGuid(), nodesManager, false);

            var script = """
                         Job:TestJob()
                         {
                             Grid.Delegate => Trace("hello");
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
        public void DelegateDispatchesSingleTaskToOneNodeTest()
        {
            var manager = new MockNodesManager();
            WitEngineSdk.Instance.Reload(Guid.NewGuid(), manager, false);

            var script = """
                         Job:TestJob()
                         {
                             Grid.Delegate => Trace("hello");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            using var resetEvent = new AutoResetEvent(false);
            task.ProcessingFinished += (_, __) => resetEvent.Set();
            task.Run();

            Assert.That(resetEvent.WaitOne(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(manager.LastBatchRequests, Is.Not.Null);
            // Exactly one task dispatched to exactly one node — not fanned out.
            Assert.That(manager.LastBatchRequests!, Has.Count.EqualTo(1));
        }

        [Test]
        public void DelegateReturnsSingleValueTest()
        {
            // The defining behavior vs ForEach: Delegate returns ONE value, not a collection.
            WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false);

            var script = """
                         Job:TestJob()
                         {
                             Object:result = Grid.Delegate => Int(10);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            using var resetEvent = new AutoResetEvent(false);
            task.ProcessingFinished += (_, __) => resetEvent.Set();
            task.Run();

            Assert.That(resetEvent.WaitOne(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {task.Status?.Message}");
            Assert.That(job.Variables["result"].Value, Is.EqualTo(10));
        }

        [Test]
        public void DelegateDoesNotSendUnrelatedOuterPoolVariablesToNodeTest()
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

                                 Grid.Delegate => Trace("hello");
                             }
                             """;
                var job = WitEngineSdk.Instance.Compile(script);
                var task = WitEngineSdk.Instance.ScheduleProcessing(job);

                using var resetEvent = new AutoResetEvent(false);
                task.ProcessingFinished += (_, __) => resetEvent.Set();
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
                        Assert.That(variableNames, Does.Not.Contain("scene"),
                            "Delegated task pool should not include unrelated outer-scope host variables.");
                        Assert.That(variableNames, Does.Not.Contain("options"),
                            "Delegated task pool should not include unrelated outer-scope host variables.");
                    });
                }
            }
            finally
            {
                WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false, null, controllersPath);
            }
        }

        [Test]
        public void DelegateIncludesReferencedHostVariablesInPoolTest()
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
                                 String:kept = String("kept-value");
                                 Object:dropped1 = Object("dropped-value-1");
                                 Object:dropped2 = Object("dropped-value-2");

                                 Grid.Delegate => Trace(kept);
                             }
                             """;
                var job = WitEngineSdk.Instance.Compile(script);
                var task = WitEngineSdk.Instance.ScheduleProcessing(job);

                using var resetEvent = new AutoResetEvent(false);
                task.ProcessingFinished += (_, __) => resetEvent.Set();
                task.Run();
                resetEvent.WaitOne();

                Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {task.Status?.Message}");
                Assert.That(manager.LastBatchRequests, Is.Not.Null);
                Assert.That(manager.LastBatchRequests, Is.Not.Empty);

                foreach (var request in manager.LastBatchRequests!)
                {
                    var variableNames = request.Pool.Select(me => me.Name).ToArray();

                    Assert.Multiple(() =>
                    {
                        Assert.That(variableNames, Contains.Item("kept"),
                            "Transformer references 'kept' — scope filter must keep it.");
                        Assert.That(variableNames, Does.Not.Contain("dropped1"),
                            "'dropped1' is declared but not referenced — scope filter must drop it.");
                        Assert.That(variableNames, Does.Not.Contain("dropped2"),
                            "'dropped2' is declared but not referenced — scope filter must drop it.");
                    });
                }
            }
            finally
            {
                WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false, null, controllersPath);
            }
        }

        [Test]
        public void DelegateWalksArrayParameterForReferencesTest()
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
                                 IntCollection:c1 = Int.Range(0, 3);
                                 IntCollection:c2 = Int.Range(10, 13);
                                 IntCollection:unused = Int.Range(100, 103);

                                 TupleCollection:result = Grid.Delegate => Zip(c1, c2);
                             }
                             """;
                var job = WitEngineSdk.Instance.Compile(script);
                var task = WitEngineSdk.Instance.ScheduleProcessing(job);

                using var resetEvent = new AutoResetEvent(false);
                task.ProcessingFinished += (_, __) => resetEvent.Set();
                task.Run();
                resetEvent.WaitOne();

                Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed),
                    $"Job did not complete cleanly: {task.Status?.Message}");
                Assert.That(manager.LastBatchRequests, Is.Not.Null);
                Assert.That(manager.LastBatchRequests, Is.Not.Empty);

                foreach (var request in manager.LastBatchRequests!)
                {
                    var variableNames = request.Pool.Select(me => me.Name).ToArray();

                    Assert.Multiple(() =>
                    {
                        Assert.That(variableNames, Contains.Item("c1"),
                            "Zip(c1, c2) — walker must descend into Values[] and find c1.");
                        Assert.That(variableNames, Contains.Item("c2"),
                            "Zip(c1, c2) — walker must descend into Values[] and find c2.");
                        Assert.That(variableNames, Does.Not.Contain("unused"),
                            "'unused' is declared but not referenced — filter must drop it.");
                    });
                }
            }
            finally
            {
                WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(), false, null, controllersPath);
            }
        }

        [Test]
        [Explicit]
        public void DelegateTest()
        {
            // Full run: host activities before/after, with the delegated activity executing on
            // the single chosen node and returning its value. Node-side Trace propagation is a
            // real-distribution concern — the in-process mock does not forward node traces
            // (ForEachTest has the identical limitation), so node execution is verified here via
            // the returned value rather than a node-side trace.
            var script = """
                         Job:TestJob()
                         {
                             Trace("before delegate");

                             Object:result = Grid.Delegate => Int(7);

                             Trace("after delegate");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);
            var messages = new List<string>();

            task.ProcessingFinished += (_, __) => resetEvent.Set();
            task.Trace += (_, mes) => messages.Add(mes);
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages, Is.EqualTo(new[] { "before delegate", "after delegate" }));
            Assert.That(job.Variables["result"].Value, Is.EqualTo(7));
        }

        [Test]
        [Explicit]
        public void DelegateWithFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Trace("before delegate");

                            Grid.Delegate => Trace("on node", true);

                            Trace("after delegate");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);
            var messages = new List<string>();

            task.ProcessingFinished += (_, __) => resetEvent.Set();
            task.Trace += (_, mes) => messages.Add(mes);
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status!.Message, Is.Not.Null);
        }

        #endregion

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
