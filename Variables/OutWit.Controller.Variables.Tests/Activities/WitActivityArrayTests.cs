using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using System;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityArrayTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };

            var activity = new WitActivityArray();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Array();"));

            activity = new WitActivityArray
            {
                Value = array
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Array([\"text\", 23, true]);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Value, Was.EqualTo(array));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Array([\"text\", 23, true]);"));
        }

        [Test]
        public void IsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };
            
            var activity = new WitActivityArray
            {
                Value = array
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, WitArray.Empty)));
        }

        [Test]
        public void CloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };
            
            var activity = new WitActivityArray
            {
                Value = array
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityArray;

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
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };
            
            var activity = new WitActivityArray
            {
                Value = array
            }.WithReturnReference("reference");

            WitActivityArray? clone = activity.MemoryPackClone();

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
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };

            var script = """
                         Job:TestJob()
                         {
                             Array:myArray = Array(["text", 23, true]);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityArray
            {
                Value = array
            }.WithReturnReference("myArray")));
            
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableArray("myArray")));
            
            script = """
                     Job:TestJob()
                     {
                         Array:myArray = ["text", 23, true];
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityArray
            {
                Value = array
            }.WithReturnReference("myArray")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableArray("myArray")));

            script = """
                     Job:TestJob()
                     {
                         myArray = Array(["text", 23, true]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityArray
            {
                Value = array
            }.WithReturnReference("myArray")));

            script = """
                     Job:TestJob()
                     {
                         Array(["text", 23, true]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityArray
            {
                Value = array
            }));
            
            script = """
                         Job:TestJob()
                         {
                             Array:myArray1 = ["text", 23, true];
                             Array:myArray2 = myArray1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myArray1"], Was.EqualTo(new WitVariableArray("myArray1")));
            Assert.That(job.Variables["myArray2"], Was.EqualTo(new WitVariableArray("myArray2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityArray
            {
                Value = array
            }.WithReturnReference("myArray1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityArray
            {
                Value = (WitReference)"myArray1"
            }.WithReturnReference("myArray2")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         Array:myArray = Array();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivityArray>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         Array:myArray = 23;
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<WitActivityArray>>(() => WitEngineSdk.Instance.Compile(script));
            
            script = """
                     Job:TestJob()
                     {
                         myArray = "text";
                     }
                     """;

            Assert.Throws<WitEngineActivityParsingException<IWitActivity>>(() => WitEngineSdk.Instance.Compile(script));
            
        }

        [Test]
        public void ProcessingTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };
            
            var script = """
                         Job:TestJob()
                         {
                             Array:myArray = Array(["text", 23, true]);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myArray"].Value, Was.EqualTo(array));

            script = """
                     Job:TestJob()
                     {
                         Array:myArray = ["text", 23, true];
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myArray"].Value, Was.EqualTo(array));
            
            script = """
                     Job:TestJob()
                     {
                         Array:myArray1 = ["text", 23, true];
                         Array:myArray2 = myArray1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myArray1"].Value, Was.EqualTo(array));
            Assert.That(job.Variables["myArray2"].Value, Was.EqualTo(array));
        }
        
    }
}
