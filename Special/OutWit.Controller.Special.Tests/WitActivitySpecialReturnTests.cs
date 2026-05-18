using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialReturnTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialReturn();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Return();"));

            activity = new WitActivitySpecialReturn
            {
                Values =
                [
                    (WitConstantString)"text",
                    (WitConstantNumeric)"10",
                    (WitReference)"obj"
                ]
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[] {
                (WitConstantString)"text",
                (WitConstantNumeric)"10",
                (WitReference)"obj"
                
            }));
            Assert.That(activity.ToString(), Is.EqualTo("Return(\"text\", 10, obj);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialReturn
            {
                Values =
                [
                    (WitConstantString)"text",
                    (WitConstantNumeric)"10",
                    (WitReference)"obj"
                ]
            };

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Values, new IWitParameter[] {
                (WitConstantString)"text",
                (WitConstantNumeric)"10",

            })));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialReturn
            {
                Values =
                [
                    (WitConstantString)"text",
                    (WitConstantNumeric)"10",
                    (WitReference)"obj"
                ]
            };

            var clone = activity.Clone() as WitActivitySpecialReturn;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[] {
                (WitConstantString)"text",
                (WitConstantNumeric)"10",
                (WitReference)"obj"

            }));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialReturn
            {
                Values =
                [
                    (WitConstantString)"text",
                    (WitConstantNumeric)"10",
                    (WitReference)"obj"
                ]
            };

            var clone = activity.MemoryPackClone() as WitActivitySpecialReturn;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[] {
                (WitConstantString)"text",
                (WitConstantNumeric)"10",
                (WitReference)"obj"

            }));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                     Job:TestJob()
                     {
                         Return("text");
                     }
                     """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialReturn
            {
                Values = [(WitConstantString)"text"]
            }));

            script = """
                     Job:TestJob()
                     {
                         Return("text", 10, obj);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialReturn
            {
                Values =
                [
                    (WitConstantString)"text",
                    (WitConstantNumeric)"10",
                    (WitReference)"obj"
                ]
            }));
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Return("text");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            var values = new List<object?>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.ReturnValue += (_, vals) =>
            {
                values.AddRange(vals);
                Console.WriteLine(string.Join(", ", vals.Select(x=>$"{x}")));
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(values.Single(), Is.EqualTo("text"));

            script = """
                     Job:TestJob()
                     {
                         Return("text", 21);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            values = new List<object?>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.ReturnValue += (_, vals) =>
            {
                values.AddRange(vals);
                Console.WriteLine(string.Join(", ", vals.Select(x => $"{x}")));
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(values[0], Is.EqualTo("text"));
            Assert.That(values[1], Is.EqualTo(21));


            script = """
                     Job:TestJob()
                     {
                        String:str = "text";
                        Return(str);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            values = new List<object?>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.ReturnValue += (_, vals) =>
            {
                values.AddRange(vals);
                Console.WriteLine(string.Join(", ", vals.Select(x => $"{x}")));
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(values.Single(), Is.EqualTo("text"));

            script = """
                     Job:TestJob()
                     {
                        String:str = "text";
                        Return(str, ["constant", 21, true]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);
            values = new List<object?>();

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.ReturnValue += (_, vals) =>
            {
                values.AddRange(vals);
                Console.WriteLine(string.Join(", ", vals.Select(x => $"{x}")));
            };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(values[0], Is.EqualTo("text"));
            Assert.That(((object?[])values[1])[0], Is.EqualTo("constant"));
            Assert.That(((object?[])values[1])[1], Is.EqualTo(21));
            Assert.That(((object?[])values[1])[2], Is.EqualTo(true));
        }
    }
}
