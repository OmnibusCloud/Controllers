using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityMatrixColumnCountTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityMatrixColumnCount();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo(null));

            activity = new WitActivityMatrixColumnCount
            {
                Matrix = (WitReference)"matrix"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.ToString(), Is.EqualTo("Matrix.ColumnCount(matrix);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Matrix.ColumnCount(matrix);"));
        }

        [Test]
        public void IsTest()
        {
            
            var activity = new WitActivityMatrixColumnCount
            {
                Matrix = (WitReference)"matrix"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Matrix,(WitReference)"matrix1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityMatrixColumnCount
            {
                Matrix = (WitReference)"matrix"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityMatrixColumnCount;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityMatrixColumnCount
            {
                Matrix = (WitReference)"matrix"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityMatrixColumnCount;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Int:ColumnCount = Matrix.ColumnCount(matrix);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixColumnCount
            {
                Matrix = (WitReference)"matrix"
            }.WithReturnReference("ColumnCount")));
            Assert.That(job.Variables.Single().Name, Was.EqualTo("ColumnCount"));
        }

        [Test]
        public async Task ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                             Int:ColumnCount = Matrix.ColumnCount(matrix);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["ColumnCount"].Value, Was.EqualTo(3));

        }
    }
}
