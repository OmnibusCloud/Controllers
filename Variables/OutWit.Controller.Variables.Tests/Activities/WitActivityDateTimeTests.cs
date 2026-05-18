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
    public class WitActivityDateTimeTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityDateTime();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Year, Was.EqualTo(null));
            Assert.That(activity.Month, Was.EqualTo(null));
            Assert.That(activity.Day, Was.EqualTo(null));
            Assert.That(activity.Hour, Was.EqualTo(null));
            Assert.That(activity.Minute, Was.EqualTo(null));
            Assert.That(activity.Second, Was.EqualTo(null));

            activity = new WitActivityDateTime
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
            Assert.That(activity.ToString(), Is.EqualTo("DateTime(\"2025/07/18\");"));

            activity = new WitActivityDateTime
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
            Assert.That(activity.ToString(), Is.EqualTo("DateTime(10, 20, 30);"));

            activity = new WitActivityDateTime
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
            Assert.That(activity.ToString(), Is.EqualTo("DateTime(10, 20, 30, 1, 2, 3);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = DateTime(10, 20, 30, 1, 2, 3);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18",
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
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
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18",
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityDateTime;

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
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18",
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityDateTime;

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
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime = DateTime("2025/07/18");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTime")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTime("myDateTime")));

            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = "2025/07/18";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTime")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTime("myDateTime")));

            script = """
                     Job:TestJob()
                     {
                         myDateTime = DateTime("2025/07/18");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTime")));

            script = """
                     Job:TestJob()
                     {
                         DateTime("2025/07/18");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18"
            }));

            script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime1 = "2025/07/18";
                             DateTime:myDateTime2 = myDateTime1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myDateTime1"], Was.EqualTo(new WitVariableDateTime("myDateTime1")));
            Assert.That(job.Variables["myDateTime2"], Was.EqualTo(new WitVariableDateTime("myDateTime2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityDateTime
            {
                Value = (WitConstantString)"2025/07/18"
            }.WithReturnReference("myDateTime1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityDateTime
            {
                Value = (WitReference)"myDateTime1"
            }.WithReturnReference("myDateTime2")));
            
            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = DateTime(10, 20, 30);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30"
            }.WithReturnReference("myDateTime")));

            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = DateTime(10, 20, 30, 1, 2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            }.WithReturnReference("myDateTime")));

            script = """
                     Job:TestJob()
                     {
                         myDateTime = DateTime(10, 20, 30, 1, 2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            }.WithReturnReference("myDateTime")));

            script = """
                     Job:TestJob()
                     {
                         DateTime(10, 20, 30, 1, 2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTime
            {
                Year = (WitConstantNumeric)"10",
                Month = (WitConstantNumeric)"20",
                Day = (WitConstantNumeric)"30",
                Hour = (WitConstantNumeric)"1",
                Minute = (WitConstantNumeric)"2",
                Second = (WitConstantNumeric)"3"
            }));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime = DateTime(2025/07/18);
                         }
                         """;

            Assert.Throws<WitEngineActivityParsingException<IWitActivity>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = DateTime(10, 20);
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDateTime>>(() => WitEngineSdk.Instance.Compile(script));

            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = DateTime(10, 20, "30");
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDateTime>>(() => WitEngineSdk.Instance.Compile(script));
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime = DateTime("2025/07/18");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Is.EqualTo(new DateTime(2025, 07, 18)));

            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = "2025/07/18";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Is.EqualTo(new DateTime(2025, 07, 18)));


            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime = "2025/07/18 15:43";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Is.EqualTo(new DateTime(2025, 07, 18, 15, 43, 00)));

            script = """
                     Job:TestJob()
                     {
                         DateTime:myDateTime1 = "2025/07/18";
                         DateTime:myDateTime2 = myDateTime1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime1"].Value, Is.EqualTo(new DateTime(2025, 07, 18)));
            Assert.That(job.Variables["myDateTime2"].Value, Is.EqualTo(new DateTime(2025, 07, 18)));
            
            script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime = DateTime(2025, 07, 21);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTime(2025, 07, 21)));

            script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime = DateTime(2025, 07, 21, 11, 22, 33);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTime(2025, 07, 21, 11, 22, 33)));

            script = """
                     Job:TestJob()
                     {
                        Int:month = 07;
                        Int:hour = 11;
                     
                        DateTime:myDateTime = DateTime(2025, month, 21, hour, 22, 33);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myDateTime"].Value, Was.EqualTo(new DateTime(2025, 07, 21, 11, 22, 33)));

            script = """
                     Job:TestJob()
                     {
                         DateTime(2025, 07, 21, 11, 22, 33);
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
                             DateTime:myDateTime = DateTime(2025, 07, -21, 11, 22, 33);
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
                                          
                        DateTime:myDateTime = DateTime(2025, month, 21, hour, 22, 33);
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
                        myDateTime = DateTime(2025, 07, 21, 11, 22, 33);
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
