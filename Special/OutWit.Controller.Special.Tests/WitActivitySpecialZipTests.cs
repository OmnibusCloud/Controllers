using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Special.Tests
{
    [TestFixture]
    public class WitActivitySpecialZipTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySpecialZip();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Zip();"));

            activity = new WitActivitySpecialZip
            {
                Values = [(WitReference)"array1", (WitReference)"array2"]
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[]{(WitReference)"array1", (WitReference)"array2"}));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("Zip(array1, array2);"));
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[]{(WitReference)"array1", (WitReference)"array2"}));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Zip(array1, array2);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySpecialZip
            {
                Values = [(WitReference)"array1", (WitReference)"array2"]
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Values, new IWitParameter[]{(WitReference)"array1"})));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySpecialZip
            {
                Values = [(WitReference)"array1", (WitReference)"array2"]
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivitySpecialZip;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[]{(WitReference)"array1", (WitReference)"array2"}));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySpecialZip
            {
                Values = [(WitReference)"array1", (WitReference)"array2"]
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivitySpecialZip;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(activity.Values, Was.EqualTo(new IWitParameter[]{(WitReference)"array1", (WitReference)"array2"}));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             TupleCollection:collection = Zip(array1, array2);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialZip
            {
                Values = [(WitReference)"array1", (WitReference)"array2"]
            }.WithReturnReference("collection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableTupleCollection("collection")));
            
            script = """
                     Job:TestJob()
                     {
                         TupleCollection:collection = Zip(array1, array2, array3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySpecialZip
            {
                Values = [(WitReference)"array1", (WitReference)"array2", (WitReference)"array3"]
            }.WithReturnReference("collection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableTupleCollection("collection")));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         TupleCollection:collection = Zip();
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivitySpecialZip>>(() => WitEngineSdk.Instance.Compile(script));
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                            Array:array1 = [1, 2, 3];
                            Array:array2 = [4, 5, 6];
                            
                            TupleCollection:collection = Zip(array1, array2);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection"], Was.EqualTo(new WitVariableTupleCollection("collection", [[1, 4], [2,5], [3,6]])));

            script = """
                     Job:TestJob()
                     {
                        IntCollection:array1 = [1, 2, 3];
                        IntCollection:array2 = [4, 5, 6];
                        
                        TupleCollection:collection = Zip(array1, array2);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection"], Was.EqualTo(new WitVariableTupleCollection("collection", [[1, 4], [2,5], [3,6]])));
            
            script = """
                     Job:TestJob()
                     {
                        TupleCollection:collection = Zip([1, 2, 3], [4, 5, 6]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection"], Was.EqualTo(new WitVariableTupleCollection("collection", [[1, 4], [2,5], [3,6]])));
            
            script = """
                     Job:TestJob()
                     {
                        IntCollection:array1 = [1, 2, 3];
                        IntCollection:array2 = [4, 5, 6];
                        IntCollection:array3 = [7, 8, 9];
                        
                        TupleCollection:collection = Zip(array1, array2, array3);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection"], Was.EqualTo(new WitVariableTupleCollection("collection", [[1, 4, 7], [2,5, 8], [3,6, 9]])));
        }
        
    }
}
