using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityVectorSparseTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityVectorSparse();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));

            activity = new WitActivityVectorSparse
            {
                Value = (WitReference)"vector"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo((WitReference)"vector"));
            Assert.That(activity.ToString(), Is.EqualTo("VectorSparse(vector);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = VectorSparse(vector);"));
        }

        [Test]
        public void IsTest()
        {
            
            var activity = new WitActivityVectorSparse
            {
                Value = (WitReference)"vector"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value,(WitReference)"vector1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityVectorSparse
            {
                Value = (WitReference)"vector"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityVectorSparse;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitReference)"vector"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityVectorSparse
            {
                Value = (WitReference)"vector"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityVectorSparse;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitReference)"vector"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             VectorSparse:myVector = vector;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVectorSparse
            {
                Value = (WitReference)"vector"
            }.WithReturnReference("myVector")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorSparse("myVector")));

            script = """
                     Job:TestJob()
                     {
                         VectorSparse:myVector = VectorSparse(vector);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVectorSparse
            {
                Value = (WitReference)"vector"
            }.WithReturnReference("myVector")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorSparse("myVector")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             VectorSparse:myVector = VectorSparse();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityVectorSparse>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var vector = WitVectorSparse<double>.Create(5, new []{(1, 1.1), (3, 3.3)});
            
            var script = """
                         Job:TestJob(VectorSparse:vector)
                         {
                             VectorSparse:myVector = vector;
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, vector);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(vector));

            script = """
                     Job:TestJob(VectorSparse:vector)
                     {
                         VectorSparse:myVector = VectorSparse(vector);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, vector);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(vector));
        }
    }
}
