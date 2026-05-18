using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Collections;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityMatrixGetColumnsTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityMatrixGetColumns();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo(null));

            activity = new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.Indices, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Matrix.GetColumns(matrix);"));

            activity = new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix",
                Indices = (WitArray)new IWitParameter[]
                {
                    (WitConstantNumeric)"1",
                    (WitConstantNumeric)"2"
                }
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.Indices, Was.EqualTo((WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2"
            }));
            Assert.That(activity.ToString(), Is.EqualTo("Matrix.GetColumns(matrix, [1, 2]);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(activity.Indices, Was.EqualTo((WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1",
                (WitConstantNumeric)"2"
            }));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Matrix.GetColumns(matrix, [1, 2]);"));
        }

        [Test]
        public void IsTest()
        {
            
            var activity = new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix",
                Indices = (WitReference)"indices"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Matrix,(WitReference)"matrix1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Indices,(WitReference)"reference1")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix",
                Indices = (WitReference)"indices"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityMatrixGetColumns;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(clone.Indices, Was.EqualTo((WitReference)"indices"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix",
                Indices = (WitReference)"indices"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityMatrixGetColumns;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Matrix, Was.EqualTo((WitReference)"matrix"));
            Assert.That(clone.Indices, Was.EqualTo((WitReference)"indices"));

        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             VectorCollection:columns = Matrix.GetColumns(matrix);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix"
            }.WithReturnReference("columns")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorCollection("columns")));

            script = """
                     Job:TestJob()
                     {
                         VectorCollection:columns = Matrix.GetColumns(matrix, [1, 2]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix",
                Indices = (WitArray)new IWitParameter[]
                {
                    (WitConstantNumeric)"1",
                    (WitConstantNumeric)"2"
                }
            }.WithReturnReference("columns")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorCollection("columns")));

            script = """
                     Job:TestJob()
                     {
                         IntCollection:indices = [1, 2];
                         VectorCollection:columns = Matrix.GetColumns(matrix, indices);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Activities.Last(), Was.EqualTo(new WitActivityMatrixGetColumns
            {
                Matrix = (WitReference)"matrix",
                Indices = (WitReference)"indices"
            }.WithReturnReference("columns")));
            Assert.That(job.Variables["columns"], Was.EqualTo(new WitVariableVectorCollection("columns")));
        }

        [Test]
        public async Task ProcessingTest()
        {
            WitVariableVectorCollection collectionLong = new WitVariableVectorCollection("columns", 
                [WitVector<double>.Create([1.1, 4.4], VectorType.Column), WitVector<double>.Create([2.2, 5.5], VectorType.Column), WitVector<double>.Create([3.3, 6.6], VectorType.Column)]);
            WitVariableVectorCollection collectionShort = new WitVariableVectorCollection("columns", 
                [WitVector<double>.Create([2.2, 5.5], VectorType.Column), WitVector<double>.Create([3.3, 6.6], VectorType.Column)]);

            var script = """
                         Job:TestJob()
                         {
                             Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                             VectorCollection:columns = Matrix.GetColumns(matrix);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["columns"], Was.EqualTo(collectionLong));

            script = """
                     Job:TestJob()
                     {
                         Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         VectorCollection:columns = Matrix.GetColumns(matrix, [0, 1, 2]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["columns"], Was.EqualTo(collectionLong));

            script = """
                     Job:TestJob()
                     {
                         IntCollection:indices = [0, 1, 2];
                         Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         VectorCollection:columns = Matrix.GetColumns(matrix, indices);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["columns"], Was.EqualTo(collectionLong));

            script = """
                     Job:TestJob()
                     {
                         Matrix:matrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         VectorCollection:columns = Matrix.GetColumns(matrix, [1, 2]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["columns"], Was.EqualTo(collectionShort));

        }
    }
}
