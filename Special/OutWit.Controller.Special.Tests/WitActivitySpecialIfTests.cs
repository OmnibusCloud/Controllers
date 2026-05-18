using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
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
    public class WitActivitySpecialIfTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialIf
            {
                Left = (WitConstantBoolean)"true",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.Left, Was.EqualTo((WitConstantBoolean)"true"));
            Assert.That(activity.Condition, Was.EqualTo(null));
            Assert.That(activity.Right, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        If(true)
                                                        {
                                                        }
                                                        """));

            activity = new WitActivitySpecialIf
            {
                Left = (WitReference)"obj",
                Condition = (WitCondition)">",
                Right = (WitConstantNumeric)"10"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(0));
            Assert.That(activity.Activities.Count, Is.EqualTo(0));
            Assert.That(activity.Variables.Count, Is.EqualTo(0));
            Assert.That(activity.Left, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Condition, Was.EqualTo((WitCondition)">"));
            Assert.That(activity.Right, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        If(obj > 10)
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
            Assert.That(activity.Left, Was.EqualTo((WitReference)"obj"));
            Assert.That(activity.Condition, Was.EqualTo((WitCondition)">"));
            Assert.That(activity.Right, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.ToString(), Is.EqualTo("""
                                                        If(obj > 10)
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
            var activity = new WitActivitySpecialIf
            {
                Left = (WitReference)"obj",
                Condition = (WitCondition)">",
                Right = (WitConstantNumeric)"10"
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
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Left, (WitReference)"obj1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Condition, (WitCondition)"<")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Right, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(new WitActivitySpecialIf
            {
                Left = (WitReference)"obj",
                Condition = (WitCondition)">",
                Right = (WitConstantNumeric)"10"
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
            var activity = new WitActivitySpecialIf
            {
                Left = (WitReference)"obj",
                Condition = (WitCondition)">",
                Right = (WitConstantNumeric)"10"
            }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.Clone() as WitActivitySpecialIf;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.Left, Was.EqualTo((WitReference)"obj"));
            Assert.That(clone.Condition, Was.EqualTo((WitCondition)">"));
            Assert.That(clone.Right, Was.EqualTo((WitConstantNumeric)"10"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialIf
                {
                    Left = (WitReference)"obj",
                    Condition = (WitCondition)">",
                    Right = (WitConstantNumeric)"10"
                }
                .WithVariable(new WitVariableObject("obj"))
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                }).WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                });

            var clone = activity.MemoryPackClone() as WitActivitySpecialIf;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(2));
            Assert.That(clone.Activities.Count, Is.EqualTo(2));
            Assert.That(clone.Variables.Count, Is.EqualTo(1));
            Assert.That(clone.Left, Was.EqualTo((WitReference)"obj"));
            Assert.That(clone.Condition, Was.EqualTo((WitCondition)">"));
            Assert.That(clone.Right, Was.EqualTo((WitConstantNumeric)"10"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             If(obj > 10)
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialIf
                {
                    Left = (WitReference)"obj",
                    Condition = (WitCondition)">",
                    Right = (WitConstantNumeric)"10"
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
                         If(obj > 10)
                         {
                            Trace("test1");
                            Trace("test2");
                            
                            If(true)
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
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialIf
                {
                    Left = (WitReference)"obj",
                    Condition = (WitCondition)">",
                    Right = (WitConstantNumeric)"10"
                }
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test1"
                })
                .WithActivity(new WitActivitySpecialTrace
                {
                    Message = (WitConstantString)"test2"
                })
                .WithActivity(new WitActivitySpecialIf
                    {
                        Left = (WitConstantBoolean)"true",
                    }
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
                            If(23)
                            {
                                Object:obj;
                                              
                                Trace("test1");
                                Trace("test2");
                            }
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivitySpecialIf>>());
        }
        
        [Test]
        public void IfGreaterOrEqualTest()
        {
            var script = """
                         Job:TestJob(Int:left, Int:right)
                         {
                             If(left >= right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(20, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 20);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }
        
                [Test]
        public void IfGreaterTest()
        {
            var script = """
                         Job:TestJob(Int:left, Int:right)
                         {
                             If(left > right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(20, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 20);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void IfLessOrEqualTest()
        {
            var script = """
                         Job:TestJob(Int:left, Int:right)
                         {
                             If(left <= right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(10, 20);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(20, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void IfLessTest()
        {
            var script = """
                         Job:TestJob(Int:left, Int:right)
                         {
                             If(left < right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(10, 20);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(20, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void IfEqualsTest()
        {
            var script = """
                         Job:TestJob(Int:left, Int:right)
                         {
                             If(left == right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(10, 20);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(20, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void IfNotEqualsTest()
        {
            var script = """
                         Job:TestJob(Int:left, Int:right)
                         {
                             If(left != right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(10, 20);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(10, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(20, 10);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
        }
        
        [Test]
        public void IfAndTest()
        {
            var script = """
                         Job:TestJob(Bool:left, Bool:right)
                         {
                             If(left && right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(true, true);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(true, false);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(false, true);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(false, false);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }

        [Test]
        public void IfOrTest()
        {
            var script = """
                         Job:TestJob(Bool:left, Bool:right)
                         {
                             If(left || right)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(true, true);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(true, false);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(false, true);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(false, false);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
        }
        
        [Test]
        public void IfTest()
        {
            var script = """
                         Job:TestJob(Bool:condition)
                         {
                             If(condition)
                             {
                                Trace("invoke");
                             }
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
            };
            task.ProgressChanged += (_, p) =>
            {
                progress.Add(p);
            };
            task.Run(true);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(progress.Count, Is.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("invoke"));
            
            messages.Clear();
            progress.Clear();
            
            started = false;
            
            task.Run(false);
            resetEvent.WaitOne();

            Assert.That(started, Is.True);
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(messages.Count, Is.EqualTo(0));
            Assert.That(progress.Count, Is.EqualTo(1));
            
            
        }

    }
}