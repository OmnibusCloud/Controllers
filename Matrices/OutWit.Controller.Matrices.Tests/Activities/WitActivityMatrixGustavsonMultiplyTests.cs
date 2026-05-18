using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Controller.Matrices.Tests.Mock;
using OutWit.Controller.Matrices.Tests.Utils;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityMatrixGustavsonMultiplyTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(Guid.NewGuid(), new MockNodesManager(),false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityMatrixGustavsonMultiply();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo(null));
            Assert.That(activity.RowIndex, Was.EqualTo(null));
            Assert.That(activity.RowVector, Was.EqualTo(null));

            activity = new WitActivityMatrixGustavsonMultiply
            {
                Matrix = (WitReference)"matrix",
                RowIndex = (WitConstantNumeric)"1",
                RowVector = (WitReference)"row"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.RowIndex, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(activity.RowVector, Was.EqualTo((WitReference)"row"));
            Assert.That(activity.ToString(), Is.EqualTo("GustavsonMultiply(1, row, matrix);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.RowIndex, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(activity.RowVector, Was.EqualTo((WitReference)"row"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = GustavsonMultiply(1, row, matrix);"));
        }

        [Test]
        public void IsTest()
        {
            
            var activity = new WitActivityMatrixGustavsonMultiply
            {
                Matrix = (WitReference)"matrix",
                RowIndex = (WitConstantNumeric)"1",
                RowVector = (WitReference)"row"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Matrix, (WitReference)"matrix1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.RowIndex, (WitConstantNumeric)"2")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.RowVector, (WitReference)"vector2")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityMatrixGustavsonMultiply
            {
                Matrix = (WitReference)"matrix",
                RowIndex = (WitConstantNumeric)"1",
                RowVector = (WitReference)"row"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityMatrixGustavsonMultiply;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(clone.RowIndex, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(clone.RowVector, Was.EqualTo((WitReference)"row"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityMatrixGustavsonMultiply
            {
                Matrix = (WitReference)"matrix",
                RowIndex = (WitConstantNumeric)"1",
                RowVector = (WitReference)"row"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityMatrixGustavsonMultiply;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(clone.RowIndex, Was.EqualTo((WitConstantNumeric)"1"));
            Assert.That(clone.RowVector, Was.EqualTo((WitReference)"row"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Tuple:rowTuple = GustavsonMultiply(1, row, matrix);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixGustavsonMultiply
            {
                Matrix = (WitReference)"matrix",
                RowIndex = (WitConstantNumeric)"1",
                RowVector = (WitReference)"row"
            }.WithReturnReference("rowTuple")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableTuple("rowTuple")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Tuple:rowTuple = GustavsonMultiply();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityMatrixGustavsonMultiply>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var matrix1 = MatrixUtils.RandomMatrix(10, 7);
            var mathNetMatrix1 = matrix1.ToMathNetMatrix();
            var matrix2 = MatrixUtils.RandomMatrix(7, 10);
            var mathNetMatrix2 = matrix2.ToMathNetMatrix();

            var mul = mathNetMatrix1 * mathNetMatrix2;

            var script = """
                         Job:TestJob(Int:rowIndex, Vector:row, Matrix:matrix)
                         {
                             Tuple:rowTuple = GustavsonMultiply(rowIndex, row, matrix);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);

            for (int i = 0; i < 10; i++)
            {
                var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, i, matrix1.GetRow(i), matrix2);

                Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
                Assert.That(job.Variables["rowTuple"], Is.InstanceOf<WitVariableTuple>());

                var result = job.Variables["rowTuple"].Value as object[];
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Length, Is.EqualTo(2));
                Assert.That(result[0], Is.EqualTo(i));
                // The adapter packs row payloads as primitive double[] for
                // cross-process safety (different plugin load contexts could
                // ship different IWitVector implementations). IWitVector<double>
                // implements IReadOnlyList<double>, so the assertion accepts
                // either shape without losing the row-content guarantee below.
                Assert.That(result[1], Is.InstanceOf<IReadOnlyList<double>>());

                var resultVector = (IReadOnlyList<double>)result[1];
                Assert.That(resultVector.ToArray(), Is.EqualTo(mul.Row(i)));
            }
        }

        [Test]
        public async Task ProcessingLoopTest()
        {
            var matrix1 = MatrixUtils.RandomMatrix(10, 7);
            var mathNetMatrix1 = matrix1.ToMathNetMatrix();
            var matrix2 = MatrixUtils.RandomMatrix(7, 10);
            var mathNetMatrix2 = matrix2.ToMathNetMatrix();

            var mul = mathNetMatrix1 * mathNetMatrix2;

            var script = """
                         Job:TestJob(Matrix:matrix1, Matrix:matrix2)
                         {
                             Int:rowCount = Matrix.RowCount(matrix1);
                             Int:columnCount = Matrix.ColumnCount(matrix2);
                             IntCollection:rowIndices = Int.Range(0, rowCount);
                             VectorCollection:rows = Matrix.GetRows(matrix1);
                             
                             TupleCollection:tasks = Zip(rowIndices, rows);
                             
                             TupleCollection:result = Transform.ForEach(task in tasks) => GustavsonMultiply(task.Item1, task.Item2, matrix2);
                             
                             MatrixSparse:resultMatrix = MatrixSparse(rowCount, columnCount, result);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, matrix1, matrix2);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["result"], Is.InstanceOf <WitVariableTupleCollection>());

            var result = job.Variables["result"].Value as IReadOnlyList<object[]?>;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(10));

            int i = 0;
            foreach (var rowResult in result)
            {
                Assert.That(rowResult, Is.Not.Null);
                Assert.That(rowResult.Length, Is.EqualTo(2));
                Assert.That(rowResult[0], Is.EqualTo(i));
                // See ProcessingTest above for the IReadOnlyList<double> rationale.
                Assert.That(rowResult[1], Is.InstanceOf<IReadOnlyList<double>>());

                var resultVector = (IReadOnlyList<double>)rowResult[1];
                Assert.That(resultVector.ToArray(), Is.EqualTo(mul.Row(i)));
                i++;
            }
            
            var resultMatrix = job.Variables["resultMatrix"].Value as WitMatrixSparse<double>;

            Assert.That(resultMatrix, Is.Not.Null);

            for (int row = 0; row < 10; row++)
            {
                for (int column = 0; column < 10; column++)
                {
                    Assert.That(resultMatrix[row, column], Is.EqualTo(mul[row, column]).Within(0.0001));
                }
            }
        }

        [Test]
        public async Task ProcessingGridTest()
        {
            var matrix1 = MatrixUtils.RandomMatrixSparseBalanced(1000, 700);
            var matrix2 = MatrixUtils.RandomMatrixSparseBalanced(700, 1000);


            var script = """
                         Job:TestJob(MatrixSparse:matrix1, MatrixSparse:matrix2)
                         {
                             Int:rowCount = Matrix.RowCount(matrix1);
                             Int:columnCount = Matrix.ColumnCount(matrix2);
                             IntCollection:rowIndices = Int.Range(0, rowCount);
                             VectorSparseCollection:rows = Matrix.GetRows(matrix1);
                             
                             TupleCollection:tasks = Zip(rowIndices, rows);
                             
                             TupleCollection:result = Grid.ForEach(task in tasks) => GustavsonMultiply(task.Item1, task.Item2, matrix2);
                             
                             MatrixSparse:resultMatrix = MatrixSparse(rowCount, columnCount, result);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, matrix1, matrix2);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["result"], Is.InstanceOf<WitVariableTupleCollection>());

            var result = job.Variables["result"].Value as IReadOnlyList<object[]?>;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1000));

            var resultMatrix = job.Variables["resultMatrix"].Value as WitMatrixSparse<double>;

            Assert.That(resultMatrix, Is.Not.Null);
        }
    }
}
