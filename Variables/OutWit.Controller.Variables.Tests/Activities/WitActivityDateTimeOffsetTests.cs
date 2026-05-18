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
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityDateTimeOffsetTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityDateTimeOffset();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Year, Was.EqualTo(null));
            Assert.That(activity.Month, Was.EqualTo(null));
            Assert.That(activity.Day, Was.EqualTo(null));
            Assert.That(activity.Hour, Was.EqualTo(null));
            Assert.That(activity.Minute, Was.EqualTo(null));
            Assert.That(activity.Second, Was.EqualTo(null));
            Assert.That(activity.Offset, Was.EqualTo(null));

            activity = new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"2025/07/18"));
            Assert.That(activity.Year, Was.EqualTo(null));
            Assert.That(activity.Month, Was.EqualTo(null));
            Assert.That(activity.Day, Was.EqualTo(null));
            Assert.That(activity.Hour, Was.EqualTo(null));
            Assert.That(activity.Minute, Was.EqualTo(null));
            Assert.That(activity.Second, Was.EqualTo(null));
            Assert.That(activity.Offset, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("DateTimeOffset(\"2025/07/18\");"));
            
            activity = new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Year, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Month, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Day, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Hour, Was.EqualTo(null));
            Assert.That(activity.Minute, Was.EqualTo(null));
            Assert.That(activity.Second, Was.EqualTo(null));
            Assert.That(activity.Offset, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("DateTimeOffset(10, 20, 30);"));

            activity = new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Year, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Month, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Day, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Hour, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(activity.Minute, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(activity.Second, Was.EqualTo((WitConstantNumeric)"3"));
            Assert.That(activity.Offset, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("DateTimeOffset(10, 20, 30, 1, 2, 3);"));

            activity = new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Year, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Month, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Day, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Hour, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(activity.Minute, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(activity.Second, Was.EqualTo((WitConstantNumeric)"3"));
            Assert.That(activity.Offset, Was.EqualTo((WitReference)"offset"));
            Assert.That(activity.ToString(), Is.EqualTo("DateTimeOffset(10, 20, 30, 1, 2, 3, offset);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = DateTimeOffset(10, 20, 30, 1, 2, 3, offset);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18",
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantString)"2025/07/19")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Year, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Month, (WitConstantNumeric)"21")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Day, (WitConstantNumeric)"31")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Hour, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Minute, (WitConstantNumeric)"21")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Second, (WitConstantNumeric)"31")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Offset, (WitReference)"offset1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18",
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityDateTimeOffset;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"2025/07/18"));
            Assert.That(clone.Year, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Month, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(clone.Day, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(clone.Hour, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(clone.Minute, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(clone.Second, Was.EqualTo((WitConstantNumeric)"3"));
            Assert.That(clone.Offset, Was.EqualTo((WitReference)"offset"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18",
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityDateTimeOffset;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"2025/07/18"));
            Assert.That(clone.Year, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Month, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(clone.Day, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(clone.Hour, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(clone.Minute, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(clone.Second, Was.EqualTo((WitConstantNumeric)"3"));
            Assert.That(clone.Offset, Was.EqualTo((WitReference)"offset"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTimeOffset = DateTimeOffset("2025/07/18");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTimeOffset")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTimeOffset("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = "2025/07/18";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTimeOffset")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTimeOffset("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         myDateTimeOffset = DateTimeOffset("2025/07/18");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset("2025/07/18");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18"
            }));

            script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTimeOffset1 = "2025/07/18";
                             DateTimeOffset:myDateTimeOffset2 = myDateTimeOffset1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myDateTimeOffset1"], Was.EqualTo(new WitVariableDateTimeOffset("myDateTimeOffset1")));
            Assert.That(job.Variables["myDateTimeOffset2"], Was.EqualTo(new WitVariableDateTimeOffset("myDateTimeOffset2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityDateTimeOffset
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTimeOffset1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityDateTimeOffset
            {
                Value = (WitReference)"myDateTimeOffset1"
            }.WithReturnReference("myDateTimeOffset2")));
            
            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = DateTimeOffset(10, 20, 30);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30"
            }.WithReturnReference("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = DateTimeOffset(10, 20, 30, 1, 2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            }.WithReturnReference("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = DateTimeOffset(10, 20, 30, 1, 2, 3, offset);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            }.WithReturnReference("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         myDateTimeOffset = DateTimeOffset(10, 20, 30, 1, 2, 3, offset);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            }.WithReturnReference("myDateTimeOffset")));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset(10, 20, 30, 1, 2, 3, offset);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffset
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3",
                Offset = (WitReference)"offset"
            }));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTimeOffset = DateTimeOffset(2025/07/18);
                         }
                         """;

            Assert.Throws<WitEngineActivityParsingException<IWitActivity>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = DateTimeOffset(10, 20);
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDateTimeOffset>>(() => WitEngineSdk.Instance.Compile(script));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = DateTimeOffset(10, 20, "30");
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDateTimeOffset>>(() => WitEngineSdk.Instance.Compile(script));
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTimeOffset = DateTimeOffset("2025/07/18");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTimeOffset"].Value, Is.EqualTo(new DateTimeOffset(new DateTime(2025, 07, 18))));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = "2025/07/18";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTimeOffset"].Value, Is.EqualTo(new DateTimeOffset(new DateTime(2025, 07, 18))));


            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset = "2025/07/18 15:43";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTimeOffset"].Value, Is.EqualTo(new DateTimeOffset(new DateTime(2025, 07, 18, 15, 43, 00))));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset:myDateTimeOffset1 = "2025/07/18";
                         DateTimeOffset:myDateTimeOffset2 = myDateTimeOffset1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTimeOffset1"].Value, Is.EqualTo(new DateTimeOffset(new DateTime(2025, 07, 18))));
            Assert.That(job.Variables["myDateTimeOffset2"].Value, Is.EqualTo(new DateTimeOffset(new DateTime(2025, 07, 18))));
            
            script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTime = DateTimeOffset(2025, 07, 21);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTimeOffset(2025, 07, 21, 0, 0, 0, TimeSpan.Zero)));

            script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTime = DateTimeOffset(2025, 07, 21, 11, 22, 33);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTimeOffset(2025, 07, 21, 11, 22, 33, TimeSpan.Zero)));

            script = """
                     Job:TestJob()
                     {
                        TimeSpan:offset = TimeSpan(3, 0, 0);
                        DateTimeOffset:myDateTime = DateTimeOffset(2025, 07, 21, 11, 22, 33, offset);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTimeOffset(2025, 07, 21, 11, 22, 33, TimeSpan.FromHours(3))));
            
            script = """
                     Job:TestJob()
                     {
                        Int:month = 07;
                        Int:hour = 11;
                     
                        DateTimeOffset:myDateTime = DateTimeOffset(2025, month, 21, hour, 22, 33);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTimeOffset(2025, 07, 21, 11, 22, 33, TimeSpan.Zero)));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffset(2025, 07, 21, 11, 22, 33);
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
                             DateTimeOffset:myDateTime = DateTimeOffset(2025, 07, -21, 11, 22, 33);
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
                        String:month = "07";
                        Int:hour = 11;
                                          
                        DateTimeOffset:myDateTime = DateTimeOffset(2025, month, 21, hour, 22, 33);
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
                        myDateTime = DateTimeOffset(2025, 07, 21, 11, 22, 33);
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
