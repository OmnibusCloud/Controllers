using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Collections;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityVectorCollectionTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityVectorCollection();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));

            activity = new WitActivityVectorCollection
            {
                Value = (WitReference)"collection"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo((WitReference)"collection"));
            Assert.That(activity.ToString(), Is.EqualTo("VectorCollection(collection);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = VectorCollection(collection);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityVectorCollection
            {
                Value = (WitReference)"collection"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantNumeric)"collection1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityVectorCollection
            {
                Value = (WitReference)"collection"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityVectorCollection;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitReference)"collection"));

        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityVectorCollection
            {
                Value = (WitReference)"collection"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityVectorCollection;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitReference)"collection"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var row1 = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
            };

            var row2 = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"4.4",
                (WitConstantNumeric)"5.5",
                (WitConstantNumeric)"6.6",
            };
            
            var array = (WitArray)new IWitParameter[]
            {
                row1,
                row2
            };

            var script = """
                         Job:TestJob()
                         {
                             VectorCollection:collection = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVectorCollection
            {
                Value = array
            }.WithReturnReference("collection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorCollection("collection")));

            script = """
                     Job:TestJob()
                     {
                         VectorCollection:collection = VectorCollection([[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVectorCollection
            {
                Value = array
            }.WithReturnReference("collection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorCollection("collection")));

            script = """
                         Job:TestJob()
                         {
                             VectorCollection:collection1 = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                             VectorCollection:collection2 = collection1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["collection1"], Was.EqualTo(new WitVariableVectorCollection("collection1")));
            Assert.That(job.Variables["collection2"], Was.EqualTo(new WitVariableVectorCollection("collection2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityVectorCollection
            {
                Value = array
            }.WithReturnReference("collection1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityVectorCollection
            {
                Value = (WitReference)"collection1"
            }.WithReturnReference("collection2")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             VectorCollection:collection = VectorCollection();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityVectorCollection>>());

            script = """
                     Job:TestJob(23)
                     {
                         VectorCollection:collection = VectorCollection();
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityVectorCollection>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            WitVariableVectorCollection collection = new WitVariableVectorCollection("collection", [WitVector<double>.Create([1.1, 2.2, 3.3]), WitVector<double>.Create([4.4, 5.5, 6.6])]);
            
            var script = """
                         Job:TestJob()
                         {
                             VectorCollection:collection = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection"], Was.EqualTo(collection));

            script = """
                     Job:TestJob()
                     {
                         VectorCollection:collection = VectorCollection([[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection"], Was.EqualTo(collection));
            
            script = """
                     Job:TestJob()
                     {
                         VectorCollection:collection1 = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         VectorCollection:collection2 = collection1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["collection1"], Was.EqualTo(collection.WithName("collection1")));
            Assert.That(job.Variables["collection2"], Was.EqualTo(collection.WithName("collection2")));
        }
    }
}
