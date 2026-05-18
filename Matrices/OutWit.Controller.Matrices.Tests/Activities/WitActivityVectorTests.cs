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
    public class WitActivityVectorTests
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
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
            };
            
            var activity = new WitActivityVector();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(null));
            Assert.That(activity.Type, Was.EqualTo(null));

            activity = new WitActivityVector
            {
                Data = array
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(array));
            Assert.That(activity.Type, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Vector([1.1, 2.2, 3.3]);"));


            activity = new WitActivityVector
            {
                Data = array,
                Type = (WitConstantString)"Row"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo(array));
            Assert.That(activity.Type, Was.EqualTo((WitConstantString)"Row"));
            Assert.That(activity.ToString(), Is.EqualTo("Vector([1.1, 2.2, 3.3], \"Row\");"));

            activity = new WitActivityVector
            {
                Data = (WitConstantNumeric)"10",
                Type = (WitConstantString)"Row"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Data, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Type, Was.EqualTo((WitConstantString)"Row"));
            Assert.That(activity.ToString(), Is.EqualTo("Vector(10, \"Row\");"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Vector(10, \"Row\");"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityVector
            {
                Data = (WitConstantNumeric)"10",
                Type = (WitConstantString)"Row"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Data, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Type, (WitConstantString)"Column")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityVector
            {
                Data = (WitConstantNumeric)"10",
                Type = (WitConstantString)"Row"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityVector;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Data, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Type, Was.EqualTo((WitConstantString)"Row"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityVector
            {
                Data = (WitConstantNumeric)"10",
                Type = (WitConstantString)"Row"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityVector;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Data, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Type, Was.EqualTo((WitConstantString)"Row"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantNumeric)"1.1",
                (WitConstantNumeric)"2.2",
                (WitConstantNumeric)"3.3",
            };
            
            var script = """
                         Job:TestJob()
                         {
                             Vector:myVector = [1.1, 2.2, 3.3];
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVector
            {
                Data = array
            }.WithReturnReference("myVector")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVector("myVector")));

            script = """
                     Job:TestJob()
                     {
                         Vector:myVector = Vector([1.1, 2.2, 3.3]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVector
            {
                Data = array
            }.WithReturnReference("myVector")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVector("myVector")));

            script = """
                     Job:TestJob()
                     {
                         Vector:myVector = Vector([1.1, 2.2, 3.3], "Row");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVector
            {
                Data = array,
                Type = (WitConstantString)"Row"
            }.WithReturnReference("myVector")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVector("myVector")));

            script = """
                     Job:TestJob()
                     {
                         Vector:myVector = Vector(10, "Row");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVector
            {
                Data = (WitConstantNumeric)"10",
                Type = (WitConstantString)"Row"
            }.WithReturnReference("myVector")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVector("myVector")));

            script = """
                     Job:TestJob()
                     {
                         myVector = Vector([1.1, 2.2, 3.3]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVector
            {
                Data = array
            }.WithReturnReference("myVector")));

            script = """
                     Job:TestJob()
                     {
                         Vector([1.1, 2.2, 3.3]);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityVector
            {
                Data = array
            }));

            script = """
                         Job:TestJob()
                         {
                             Vector:myVector1 = [1.1, 2.2, 3.3];
                             Vector:myVector2 = myVector1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myVector1"], Was.EqualTo(new WitVariableVector("myVector1")));
            Assert.That(job.Variables["myVector2"], Was.EqualTo(new WitVariableVector("myVector2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityVector
            {
                Data = array
            }.WithReturnReference("myVector1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityVector
            {
                Data = (WitReference)"myVector1"
            }.WithReturnReference("myVector2")));

            
            script = """
                         Job:TestJob()
                         {
                             DoubleCollection:collection = [1.1, 2.2, 3.3];
                             Vector:myVector2 = collection;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myVector2"], Was.EqualTo(new WitVariableVector("myVector2")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityVector
            {
                Data = (WitReference)"collection"
            }.WithReturnReference("myVector2")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Vector:myVector = Vector();
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityVector>>());
        }

        [Test]
        public async Task ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Vector:myVector = [1.1, 2.2, 3.3];
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3], VectorType.Row)));

            script = """
                     Job:TestJob()
                     {
                         Vector:myVector = Vector([1.1, 2.2, 3.3]);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3], VectorType.Row)));

            script = """
                     Job:TestJob()
                     {
                         Vector:myVector = Vector([1.1, 2.2, 3.3], "Column");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3], VectorType.Column)));

            script = """
                     Job:TestJob()
                     {
                         Vector:myVector = Vector(10, "Column");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(WitVector<double>.Create(10, VectorType.Column)));
            
            script = """
                     Job:TestJob()
                     {
                         Vector:myVector1 = [1.1, 2.2, 3.3];
                         Vector:myVector2 = myVector1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector1"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3])));
            Assert.That(job.Variables["myVector2"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3])));

            script = """
                     Job:TestJob()
                     {
                         DoubleCollection:collection = [1.1, 2.2, 3.3];
                         Vector:myVector = collection;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(WitVector<double>.Create([1.1, 2.2, 3.3])));

            script = """
                     Job:TestJob()
                     {
                         Int:size = 10;
                         Vector:myVector = Vector(size, "Row");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myVector"].Value, Was.EqualTo(WitVector<double>.Create(10, VectorType.Row)));
        }

        [Test]
        public void ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Vector:myVector = Vector([1.1, "2.2", 3.3]);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }
    }
}
