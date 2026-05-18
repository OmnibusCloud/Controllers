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
    public class WitActivityDoubleCollectionTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityDoubleCollection();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("DoubleCollection();"));

            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2",
                (WitConstantNumeric)"3",
            };

            activity = new WitActivityDoubleCollection
            {
                Value = array

            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("DoubleCollection([1, 2, 3]);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = DoubleCollection([1, 2, 3]);"));
        }

        [Test]
        public void IsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2",
                (WitConstantNumeric)"3",
            };
            
            var activity = new WitActivityDoubleCollection
            {
                Value = array
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2",
            })));
        }

        [Test]
        public void CloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2",
                (WitConstantNumeric)"3",
            };
            
            var activity = new WitActivityDoubleCollection
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityDoubleCollection;

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
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2",
                (WitConstantNumeric)"3",
            };
            
            var activity = new WitActivityDoubleCollection
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityDoubleCollection;

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
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2",
                (WitConstantNumeric)"3",
            };
            
            var script = """
                         Job:TestJob()
                         {
                             DoubleCollection:myCollection = DoubleCollection([1, 2, 3]);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDoubleCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDoubleCollection("myCollection")));
            
            script = """
                     Job:TestJob()
                     {
                         DoubleCollection:myCollection = [1, 2, 3];
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDoubleCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDoubleCollection("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         myCollection = DoubleCollection([1, 2, 3]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDoubleCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         DoubleCollection([1, 2, 3]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityDoubleCollection
            {
                Value = array.Clone()
            }));
            
            script = """
                         Job:TestJob()
                         {
                             DoubleCollection:myCollection1 = [1, 2, 3];
                             DoubleCollection:myCollection2 = myCollection1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myCollection1"], Was.EqualTo(new WitVariableDoubleCollection("myCollection1")));
            Assert.That(job.Variables["myCollection2"], Was.EqualTo(new WitVariableDoubleCollection("myCollection2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityDoubleCollection
            {
                Value = array.Clone()
            }.WithReturnReference("myCollection1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityDoubleCollection
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
                         DoubleCollection:myCollection = DoubleCollection();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityDoubleCollection>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         DoubleCollection:myCollection = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityDoubleCollection>>(() => WitEngineSdk.Instance.Compile(script));
            
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
                             DoubleCollection:myCollection = DoubleCollection([1, 2, 3]);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"].Value, Is.EqualTo(new WitVariableDoubleCollection("myCollection", [1, 2, 3])));

            script = """
                     Job:TestJob()
                     {
                         DoubleCollection:myCollection = [1, 2, 3];
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"].Value, Is.EqualTo(new WitVariableDoubleCollection("myCollection", [1, 2, 3])));

            script = """
                     Job:TestJob()
                     {
                         DoubleCollection:myCollection1 = [1, 2, 3];
                         DoubleCollection:myCollection2 = myCollection1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection1"].Value, Is.EqualTo(new WitVariableDoubleCollection("myCollection", [1, 2, 3])));
            Assert.That(job.Variables["myCollection2"].Value, Is.EqualTo(new WitVariableDoubleCollection("myCollection", [1, 2, 3])));
        }
        
    }
}
