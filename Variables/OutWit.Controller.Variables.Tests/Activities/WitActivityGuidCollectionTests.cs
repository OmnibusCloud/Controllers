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
    public class WitActivityGuidCollectionTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityGuidCollection();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("GuidCollection();"));

            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6",
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7",
            };

            activity = new WitActivityGuidCollection
            {
                Value = array

            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("GuidCollection([\"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6\", \"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7\"]);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = GuidCollection([\"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6\", \"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7\"]);"));
        }

        [Test]
        public void IsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6",
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7",
            };
            
            var activity = new WitActivityGuidCollection
            {
                Value = array
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitArray)new IWitParameter[]
            {
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6",
            })));
        }

        [Test]
        public void CloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6",
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7",
            };
            
            var activity = new WitActivityGuidCollection
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityGuidCollection;

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
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6",
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7",
            };
            
            var activity = new WitActivityGuidCollection
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityGuidCollection;

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
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6",
                (WitConstantString)"BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7",
            };
            
            var script = """
                         Job:TestJob()
                         {
                             GuidCollection:myCollection = GuidCollection(["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"]);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuidCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableGuidCollection("myCollection")));
            
            script = """
                     Job:TestJob()
                     {
                         GuidCollection:myCollection = ["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"];
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuidCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableGuidCollection("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         myCollection = GuidCollection(["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuidCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         GuidCollection(["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityGuidCollection
            {
                Value = array.Clone()
            }));
            
            script = """
                         Job:TestJob()
                         {
                             GuidCollection:myCollection1 = ["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"];
                             GuidCollection:myCollection2 = myCollection1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myCollection1"], Was.EqualTo(new WitVariableGuidCollection("myCollection1")));
            Assert.That(job.Variables["myCollection2"], Was.EqualTo(new WitVariableGuidCollection("myCollection2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityGuidCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityGuidCollection
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
                         GuidCollection:myCollection = GuidCollection();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityGuidCollection>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         GuidCollection:myCollection = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityGuidCollection>>(() => WitEngineSdk.Instance.Compile(script));
            
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
                             GuidCollection:myCollection = GuidCollection(["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"]);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"], Is.EqualTo(new WitVariableGuidCollection("myCollection", [Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6"), Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7")])));

            script = """
                     Job:TestJob()
                     {
                         GuidCollection:myCollection = ["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"];
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"].Value, Is.EqualTo(new WitVariableGuidCollection("myCollection", [Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6"), Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7")])));

            script = """
                     Job:TestJob()
                     {
                         GuidCollection:myCollection1 = ["BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6", "BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7"];
                         GuidCollection:myCollection2 = myCollection1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection1"].Value, Is.EqualTo(new WitVariableGuidCollection("myCollection", [Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6"), Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7")])));
            Assert.That(job.Variables["myCollection2"].Value, Is.EqualTo(new WitVariableGuidCollection("myCollection", [Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B6"), Guid.Parse("BC12F10D-2B5B-4122-B7D0-AB99CCBBB3B7")])));
        }
        
    }
}
