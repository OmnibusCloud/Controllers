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
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityMatrixGetRowTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityMatrixGetRow();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo(null));

            activity = new WitActivityMatrixGetRow
            {
                Matrix = (WitReference)"matrix",
                Index = (WitConstantNumeric)"1"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.Index, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(activity.ToString(), Is.EqualTo("Matrix.GetRow(matrix, 1);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.Index, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Matrix.GetRow(matrix, 1);"));
        }

        [Test]
        public void IsTest()
        {
            
            var activity = new WitActivityMatrixGetRow
            {
                Matrix = (WitReference)"matrix",
                Index = (WitConstantNumeric)"1"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Matrix,(WitReference)"matrix1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Index,(WitConstantNumeric)"2")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityMatrixGetRow
            {
                Matrix = (WitReference)"matrix",
                Index = (WitConstantNumeric)"1"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityMatrixGetRow;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(clone.Index, Was.EqualTo((WitConstantNumeric)"1"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityMatrixGetRow
            {
                Matrix = (WitReference)"matrix",
                Index = (WitConstantNumeric)"1"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityMatrixGetRow;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(clone.Index, Was.EqualTo((WitConstantNumeric)"1"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Vector:row = Matrix.GetRow(matrix, 1);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixGetRow
            {
                Matrix = (WitReference)"matrix",
                Index = (WitConstantNumeric)"1"
            }.WithReturnReference("row")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVector("row")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Vector:row = Matrix.GetRow();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityMatrixGetRow>>());

            script = """
                     Job:TestJob()
                     {
                         Vector:row = Matrix.GetRow(matrix);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityMatrixGetRow>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                             Vector:row = Matrix.GetRow(matrix, 0);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["row"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3], VectorType.Row)));

            script = """
                     Job:TestJob()
                     {
                         Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         Vector:row = Matrix.GetRow(matrix, 1);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["row"].Value, Was.EqualTo(WitVector<double>.Create([4.4, 5.5, 6.6], VectorType.Row)));

        }
    }
}
