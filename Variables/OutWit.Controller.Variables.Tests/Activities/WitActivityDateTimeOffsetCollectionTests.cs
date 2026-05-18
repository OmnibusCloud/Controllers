using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using System;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Variables.Collections;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityDateTimeOffsetCollectionTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityDateTimeOffsetCollection();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);

            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"2025/07/18",
                (WitConstantString)"2025/08/06",
            };

            activity = new WitActivityDateTimeOffsetCollection
            {
                Value = array

            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("DateTimeOffsetCollection([\"2025/07/18\", \"2025/08/06\"]);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = DateTimeOffsetCollection([\"2025/07/18\", \"2025/08/06\"]);"));
        }

        [Test]
        public void IsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"2025/07/18",
                (WitConstantString)"2025/08/06",
            };
            
            var activity = new WitActivityDateTimeOffsetCollection
            {
                Value = array
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitArray)new IWitParameter[]
            {
                (WitConstantString)"2025/07/18",
            })));
        }

        [Test]
        public void CloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"2025/07/18",
                (WitConstantString)"2025/08/06",
            };
            
            var activity = new WitActivityDateTimeOffsetCollection
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityDateTimeOffsetCollection;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo(array));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"2025/07/18",
                (WitConstantString)"2025/08/06",
            };
            
            var activity = new WitActivityDateTimeOffsetCollection
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityDateTimeOffsetCollection;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(clone.Value, Was.EqualTo(array));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"2025/07/18",
                (WitConstantString)"2025/08/06",
            };
            
            var script = """
                         Job:TestJob()
                         {
                             DateTimeOffsetCollection:myCollection = DateTimeOffsetCollection(["2025/07/18", "2025/08/06"]);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffsetCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection")));
            
            script = """
                     Job:TestJob()
                     {
                         DateTimeOffsetCollection:myCollection = ["2025/07/18", "2025/08/06"];
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffsetCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         myCollection = DateTimeOffsetCollection(["2025/07/18", "2025/08/06"]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffsetCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffsetCollection(["2025/07/18", "2025/08/06"]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDateTimeOffsetCollection
            {
                Value = array.Clone()
            }));
            
            script = """
                         Job:TestJob()
                         {
                             DateTimeOffsetCollection:myCollection1 = ["2025/07/18", "2025/08/06"];
                             DateTimeOffsetCollection:myCollection2 = myCollection1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myCollection1"], Was.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection1")));
            Assert.That(job.Variables["myCollection2"], Was.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityDateTimeOffsetCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityDateTimeOffsetCollection
            {
                Value = (WitReference)"myCollection1"
            }.WithReturnReference("myCollection2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         DateTimeOffsetCollection:myCollection = DateTimeOffsetCollection();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityDateTimeOffsetCollection>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         DateTimeOffsetCollection:myCollection = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDateTimeOffsetCollection>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myCollection = 23;
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<IWitActivity>>(() => WitEngineSdk.Instance.Compile(script));
            
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTimeOffsetCollection:myCollection = DateTimeOffsetCollection(["2025/07/18", "2025/08/06"]);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"], Is.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection", [DateTimeOffset.Parse("2025/07/18"), DateTimeOffset.Parse("2025/08/06")])));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffsetCollection:myCollection = ["2025/07/18", "2025/08/06"];
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"].Value, Is.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection", [DateTimeOffset.Parse("2025/07/18"), DateTimeOffset.Parse("2025/08/06")])));

            script = """
                     Job:TestJob()
                     {
                         DateTimeOffsetCollection:myCollection1 = ["2025/07/18", "2025/08/06"];
                         DateTimeOffsetCollection:myCollection2 = myCollection1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection1"].Value, Is.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection", [DateTimeOffset.Parse("2025/07/18"), DateTimeOffset.Parse("2025/08/06")])));
            Assert.That(job.Variables["myCollection2"].Value, Is.EqualTo(new WitVariableDateTimeOffsetCollection("myCollection", [DateTimeOffset.Parse("2025/07/18"), DateTimeOffset.Parse("2025/08/06")])));
        }
        
    }
}
