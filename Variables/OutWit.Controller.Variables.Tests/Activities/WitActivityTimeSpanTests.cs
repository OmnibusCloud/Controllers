using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Common.MemoryPack;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityTimeSpanTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityTimeSpan();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Days, Was.EqualTo(null));
            Assert.That(activity.Hours, Was.EqualTo(null));
            Assert.That(activity.Minutes, Was.EqualTo(null));
            Assert.That(activity.Seconds, Was.EqualTo(null));
            Assert.That(activity.Milliseconds, Was.EqualTo(null));
            Assert.That(activity.ReturnReference, Is.Null);

            activity = new WitActivityTimeSpan
            {
                Value = (WitConstantString)"01:02:03"
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"01:02:03"));
            Assert.That(activity.Days, Was.EqualTo(null));
            Assert.That(activity.Hours, Was.EqualTo(null));
            Assert.That(activity.Minutes, Was.EqualTo(null));
            Assert.That(activity.Seconds, Was.EqualTo(null));
            Assert.That(activity.Milliseconds, Was.EqualTo(null));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("TimeSpan(\"01:02:03\");"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"01:02:03"));
            Assert.That(activity.Days, Was.EqualTo(null));
            Assert.That(activity.Hours, Was.EqualTo(null));
            Assert.That(activity.Minutes, Was.EqualTo(null));
            Assert.That(activity.Seconds, Was.EqualTo(null));
            Assert.That(activity.Milliseconds, Was.EqualTo(null));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = TimeSpan(\"01:02:03\");"));
            
            activity = new WitActivityTimeSpan
            {
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Days, Was.EqualTo(null));
            Assert.That(activity.Hours, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Minutes, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Seconds, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Milliseconds, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("TimeSpan(10, 20, 30);"));

            activity = new WitActivityTimeSpan
            {
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Days, Was.EqualTo((WitConstantNumeric)"5"));
            Assert.That(activity.Hours, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Minutes, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Seconds, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Milliseconds, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("TimeSpan(5, 10, 20, 30);"));

            activity = new WitActivityTimeSpan
            {
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Days, Was.EqualTo((WitConstantNumeric)"5"));
            Assert.That(activity.Hours, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Minutes, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Seconds, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Milliseconds, Was.EqualTo((WitConstantNumeric)"40"));
            Assert.That(activity.ToString(), Is.EqualTo("TimeSpan(5, 10, 20, 30, 40);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = TimeSpan(5, 10, 20, 30, 40);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityTimeSpan
            {
                Value = (WitConstantString)"01:02:03",
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantString)"01:02:04")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Days, (WitConstantNumeric)"6")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Hours, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Minutes, (WitConstantNumeric)"21")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Seconds, (WitConstantNumeric)"31")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Milliseconds, (WitConstantNumeric)"41")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityTimeSpan
            {
                Value = (WitConstantString)"01:02:03",
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityTimeSpan;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"01:02:03"));
            Assert.That(clone.Days, Was.EqualTo((WitConstantNumeric)"5"));
            Assert.That(clone.Hours, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Minutes, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(clone.Seconds, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(clone.Milliseconds, Was.EqualTo((WitConstantNumeric)"40"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityTimeSpan
            {
                Value = (WitConstantString)"01:02:03",
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityTimeSpan;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"01:02:03"));
            Assert.That(clone.Days, Was.EqualTo((WitConstantNumeric)"5"));
            Assert.That(clone.Hours, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Minutes, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(clone.Seconds, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(clone.Milliseconds, Was.EqualTo((WitConstantNumeric)"40"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan("6:12:14");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Value = (WitConstantString)"6:12:14"
            }.WithReturnReference("myTimeSpan")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableTimeSpan("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = "6:12:14";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Value = (WitConstantString)"6:12:14"
            }.WithReturnReference("myTimeSpan")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableTimeSpan("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         myTimeSpan = TimeSpan("6:12:14");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Value = (WitConstantString)"6:12:14"
            }.WithReturnReference("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan("6:12:14");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Value = (WitConstantString)"6:12:14"
            }));

            script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan1 = "6:12:14";
                             TimeSpan:myTimeSpan2 = myTimeSpan1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myTimeSpan1"], Was.EqualTo(new WitVariableTimeSpan("myTimeSpan1")));
            Assert.That(job.Variables["myTimeSpan2"], Was.EqualTo(new WitVariableTimeSpan("myTimeSpan2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityTimeSpan
            {
                Value = (WitConstantString)"6:12:14"
            }.WithReturnReference("myTimeSpan1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityTimeSpan
            {
                Value = (WitReference)"myTimeSpan1"
            }.WithReturnReference("myTimeSpan2")));
            
            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = TimeSpan(10, 20, 30);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30"
            }.WithReturnReference("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = TimeSpan(5, 10, 20, 30);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30"
            }.WithReturnReference("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = TimeSpan(5, 10, 20, 30, 40);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            }.WithReturnReference("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         myTimeSpan = TimeSpan(5, 10, 20, 30, 40);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            }.WithReturnReference("myTimeSpan")));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan(5, 10, 20, 30, 40);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityTimeSpan
            {
                Days = (WitConstantNumeric)"5",
                Hours = (WitConstantNumeric)"10",
                Minutes = (WitConstantNumeric)"20",
                Seconds = (WitConstantNumeric)"30",
                Milliseconds = (WitConstantNumeric)"40"
            }));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan(6:12:14);
                         }
                         """;

            Assert.Throws<WitEngineActivityParsingException<IWitActivity>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = TimeSpan(10, 20);
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityTimeSpan>>(() => WitEngineSdk.Instance.Compile(script));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = TimeSpan(10, 20, "30");
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityTimeSpan>>(() => WitEngineSdk.Instance.Compile(script));
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan("6:12:14");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan"].Value, Is.EqualTo(new TimeSpan(6, 12, 14)));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan = "6:12:14";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan"].Value, Is.EqualTo(new TimeSpan(6, 12, 14)));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan:myTimeSpan1 = "6:12:14";
                         TimeSpan:myTimeSpan2 = myTimeSpan1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan1"].Value, Is.EqualTo(new TimeSpan(6, 12, 14)));
            Assert.That(job.Variables["myTimeSpan2"].Value, Is.EqualTo(new TimeSpan(6, 12, 14)));

            script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan(10, 20, 30);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan"].Value, Was.EqualTo(new TimeSpan(10, 20, 30)));

            script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan(5, 10, 20, 30);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan"].Value, Was.EqualTo(new TimeSpan(5, 10, 20, 30)));

            script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan(5, 10, 20, 30, 40);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan"].Value, Was.EqualTo(new TimeSpan(5, 10, 20, 30, 40)));

            script = """
                     Job:TestJob()
                     {
                        Int:hours = 5;
                        Int:seconds = 30;
                     
                        TimeSpan:myTimeSpan = TimeSpan(hours, 10, 20, seconds, 40);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myTimeSpan"].Value, Was.EqualTo(new TimeSpan(5, 10, 20, 30, 40)));

            script = """
                     Job:TestJob()
                     {
                         TimeSpan(5, 10, 20, 30, 40);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
        }

        [Test]
        public void ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan = TimeSpan(5, -10, 20, 30, 40);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);

            script = """
                     Job:TestJob()
                     {
                        String:hours = "10";
                        Int:seconds = 30;
                     
                        TimeSpan:myTimeSpan = TimeSpan(5, hours, 20, seconds, 40);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);

            script = """
                     Job:TestJob()
                     {
                        myTimeSpan = TimeSpan(5, 10, 20, 30, 40);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }
    }
}
