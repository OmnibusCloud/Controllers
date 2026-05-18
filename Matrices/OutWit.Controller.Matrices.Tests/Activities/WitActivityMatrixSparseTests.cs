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
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityMatrixSparseTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityMatrixSparse();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(null));
            Assert.That(activity.Rows, Was.EqualTo(null));
            Assert.That(activity.Columns, Was.EqualTo(null));

            activity = new WitActivityMatrixSparse
            {
                Data = (WitReference)"matrix"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.Rows, Was.EqualTo(null));
            Assert.That(activity.Columns, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("MatrixSparse(matrix);"));
            activity = new WitActivityMatrixSparse
            {
                Data = (WitReference)"tuple",
                Rows = (WitConstantNumeric)"10",
                Columns = (WitConstantNumeric)"7",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo((WitReference)"tuple"));
            Assert.That(activity.Rows, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Columns, Was.EqualTo((WitConstantNumeric)"7"));
            Assert.That(activity.ToString(), Is.EqualTo("MatrixSparse(10, 7, tuple);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = MatrixSparse(10, 7, tuple);"));
        }

        [Test]
        public void IsTest()
        {
            
            var activity = new WitActivityMatrixSparse
            {
                Data = (WitReference)"tuple",
                Rows = (WitConstantNumeric)"10",
                Columns = (WitConstantNumeric)"7",
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Data,(WitReference)"tuple1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Rows,(WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Columns,(WitConstantNumeric)"8")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityMatrixSparse
            {
                Data = (WitReference)"tuple",
                Rows = (WitConstantNumeric)"10",
                Columns = (WitConstantNumeric)"7",
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityMatrixSparse;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Data, Was.EqualTo((WitReference)"tuple"));
            Assert.That(clone.Rows, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Columns, Was.EqualTo((WitConstantNumeric)"7"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityMatrixSparse
            {
                Data = (WitReference)"tuple",
                Rows = (WitConstantNumeric)"10",
                Columns = (WitConstantNumeric)"7",
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityMatrixSparse;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Data, Was.EqualTo((WitReference)"tuple"));
            Assert.That(clone.Rows, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Columns, Was.EqualTo((WitConstantNumeric)"7"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             MatrixSparse:myMatrix = matrix;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixSparse
            {
                Data = (WitReference)"matrix"
            }.WithReturnReference("myMatrix")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrixSparse("myMatrix")));

            script = """
                     Job:TestJob()
                     {
                         MatrixSparse:myMatrix = MatrixSparse(matrix);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixSparse
            {
                Data = (WitReference)"matrix"
            }.WithReturnReference("myMatrix")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrixSparse("myMatrix")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             MatrixSparse:myMatrix = MatrixSparse();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityMatrixSparse>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var elements = new List<(int r, int c, double val)>
            {
                (0, 1, 1.1),
                (0, 3, 2.2),
                (2, 2, 3.3),
                (3, 0, 4.4),
                (3, 4, 5.5)
            };

            var matrix = WitMatrixSparse<double>.Create(4, 5, elements);
            
            var script = """
                         Job:TestJob(MatrixSparse:matrix)
                         {
                             MatrixSparse:myMatrix = matrix;
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, matrix);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));
            
            
            var tuple = new List<object?[]>
            {
                new object?[] { 0, WitVectorSparse<double>.Create(5, new [] { (1, 1.1), (3, 2.2)}) },
                new object?[] { 2, WitVectorSparse<double>.Create(5, new [] { (2, 3.3)}) },
                new object?[] { 3, WitVectorSparse<double>.Create(5, new [] { (0, 4.4), (4, 5.5)}) }
            };

            script = """
                     Job:TestJob(TupleCollection:tuple)
                     {
                         MatrixSparse:myMatrix = MatrixSparse(4, 5, tuple);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, tuple);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));

            tuple = new List<object?[]>
            {
                new object?[] { 0, 1, 1.1 },
                new object?[] { 0, 3, 2.2 },
                new object?[] { 2, 2, 3.3 },
                new object?[] { 3, 0, 4.4 },
                new object?[] { 3, 4, 5.5 }
            };

            script = """
                     Job:TestJob(TupleCollection:tuple)
                     {
                         MatrixSparse:myMatrix = MatrixSparse(4, 5, tuple);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, tuple);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));


            script = """
                     Job:TestJob(MatrixSparse:matrix)
                     {
                         MatrixSparse:myMatrix = MatrixSparse(matrix);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, matrix);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));
        }
    }
}
