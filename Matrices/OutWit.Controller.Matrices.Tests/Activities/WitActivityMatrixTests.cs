using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Matrices.Tests.Activities
{
    [TestFixture]
    public class WitActivityMatrixTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
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
            
            var array1 = (WitArray)new IWitParameter[]
            {
                row1,
                row2
            };

            var array2 = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
                (WitConstantNumeric)"4.4",
                (WitConstantNumeric)"5.5",
                (WitConstantNumeric)"6.6",
            };

            var activity = new WitActivityMatrix();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(null));
            Assert.That(activity.Rows, Was.EqualTo(null));
            Assert.That(activity.Columns, Was.EqualTo(null));

            activity = new WitActivityMatrix
            {
                Data = array1
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(array1));
            Assert.That(activity.Rows, Was.EqualTo(null));
            Assert.That(activity.Columns, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Matrix([[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]]);"));


            activity = new WitActivityMatrix
            {
                Data = array2,
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(array2));
            Assert.That(activity.Rows, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(activity.Columns, Was.EqualTo((WitConstantNumeric)"3"));
            Assert.That(activity.ToString(), Is.EqualTo("Matrix(2, 3, [1.1, 2.2, 3.3, 4.4, 5.5, 6.6]);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Matrix(2, 3, [1.1, 2.2, 3.3, 4.4, 5.5, 6.6]);"));
        }

        [Test]
        public void IsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
                (WitConstantNumeric)"4.4",
                (WitConstantNumeric)"5.5",
                (WitConstantNumeric)"6.6",
            };
            
            var activity = new WitActivityMatrix
            {
                Data = array,
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Rows, (WitConstantNumeric)"3")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Columns, (WitConstantNumeric)"4")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Data, WitArray.Empty)));
        }

        [Test]
        public void CloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
                (WitConstantNumeric)"4.4",
                (WitConstantNumeric)"5.5",
                (WitConstantNumeric)"6.6",
            };

            var activity = new WitActivityMatrix
            {
                Data = array,
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityMatrix;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Data, Was.EqualTo(array));
            Assert.That(clone.Rows, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(clone.Columns, Was.EqualTo((WitConstantNumeric)"3"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
                (WitConstantNumeric)"4.4",
                (WitConstantNumeric)"5.5",
                (WitConstantNumeric)"6.6",
            };

            var activity = new WitActivityMatrix
            {
                Data = array,
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityMatrix;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Data, Was.EqualTo(array));
            Assert.That(clone.Rows, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(clone.Columns, Was.EqualTo((WitConstantNumeric)"3"));
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

            var array1 = (WitArray)new IWitParameter[]
            {
                row1,
                row2
            };

            var array2 = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
                (WitConstantNumeric)"4.4",
                (WitConstantNumeric)"5.5",
                (WitConstantNumeric)"6.6",
            };

            var script = """
                         Job:TestJob()
                         {
                             Matrix:myMatrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrix
            {
                Data = array1
            }.WithReturnReference("myMatrix")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrix("myMatrix")));

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix([[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrix
            {
                Data = array1
            }.WithReturnReference("myMatrix")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrix("myMatrix")));

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix(2, 3, [1.1, 2.2, 3.3, 4.4, 5.5, 6.6]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrix
            {
                Data = array2,
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }.WithReturnReference("myMatrix")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrix("myMatrix")));

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix(2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrix
            {
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }.WithReturnReference("myMatrix")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableMatrix("myMatrix")));

            script = """
                     Job:TestJob()
                     {
                         myMatrix = Matrix(2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrix
            {
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }.WithReturnReference("myMatrix")));

            script = """
                     Job:TestJob()
                     {
                         Matrix(2, 3);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityMatrix
            {
                Rows = (WitConstantNumeric)"2",
                Columns = (WitConstantNumeric)"3"
            }));

            script = """
                         Job:TestJob()
                         {
                             Matrix:myMatrix1 = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                             Matrix:myMatrix2 = myMatrix1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myMatrix1"], Was.EqualTo(new WitVariableMatrix("myMatrix1")));
            Assert.That(job.Variables["myMatrix2"], Was.EqualTo(new WitVariableMatrix("myMatrix2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityMatrix
            {
                Data = array1
            }.WithReturnReference("myMatrix1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityMatrix
            {
                Data = (WitReference)"myMatrix1"
            }.WithReturnReference("myMatrix2")));

            
            script = """
                         Job:TestJob()
                         {
                             VectorCollection:collection = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                             Matrix:myMatrix2 = collection;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myMatrix2"], Was.EqualTo(new WitVariableMatrix("myMatrix2")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityMatrix
            {
                Data = (WitReference)"collection"
            }.WithReturnReference("myMatrix2")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Matrix:myMatrix = Matrix();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityMatrix>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var matrix = WitMatrix<double>.Create(2, 3, [1.1, 2.2, 3.3, 4.4, 5.5, 6.6]);
            var matrixEmpty = WitMatrix<double>.Create(2, 3);

            var script = """
                         Job:TestJob()
                         {
                             Matrix:myMatrix = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix([[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix(2, 3, [1.1, 2.2, 3.3, 4.4, 5.5, 6.6]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix(2, 3);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrixEmpty));
            
            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix1 = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         Matrix:myMatrix2 = myMatrix1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix1"].Value, Was.EqualTo(matrix));
            Assert.That(job.Variables["myMatrix2"].Value, Was.EqualTo(matrix));

            script = """
                     Job:TestJob()
                     {
                         VectorCollection:collection = [[1.1, 2.2, 3.3], [4.4, 5.5, 6.6]];
                         Matrix:myMatrix = collection;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myMatrix"].Value, Was.EqualTo(matrix));
        }

        [Test]
        public async Task ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Matrix:myMatrix = [[1.1, 2.2, 3.3], [4.4, 5.5]];
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Is.Not.Null);
            Console.WriteLine(status.Message);

            script = """
                     Job:TestJob()
                     {
                         Matrix:myMatrix = Matrix(2, 3, [1.1, 2.2, 3.3, 4.4, 5.5]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Is.Not.Null);
            Console.WriteLine(status.Message);
        }
    }
}
