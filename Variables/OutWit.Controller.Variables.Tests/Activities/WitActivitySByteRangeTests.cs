using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using System.Linq;
using System.Threading;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Variables.Collections;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivitySByteRangeTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivitySByteRange();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.From, Is.Null);
            Assert.That(activity.To, Is.Null);
            Assert.That(activity.Step, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);

            activity = new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
            };

            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.From, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.To, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Step, Is.Null);
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.ToString(), Is.EqualTo("SByte.Range(10, 20);"));
            
            activity = new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
                Step = (WitConstantNumeric)"2",
            };
            
            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.From, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.To, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Step, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = SByte.Range(10, 20, 2);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
                Step = (WitConstantNumeric)"2",
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(range => range.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(range => range.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(range => range.From, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(range => range.To, (WitConstantNumeric)"22")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(range => range.Step, (WitConstantNumeric)"3")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
                Step = (WitConstantNumeric)"2",
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivitySByteRange;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(activity.From, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.To, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Step, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
                Step = (WitConstantNumeric)"2",
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivitySByteRange;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Was.EqualTo(1));
            Assert.That(activity.From, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.To, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Step, Was.EqualTo((WitConstantNumeric)"2"));
            Assert.That(clone.ReturnReference, Was.EqualTo("reference"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             SByteCollection:myCollection = SByte.Range(10, 20);
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableSByteCollection("myCollection")));
            
            script = """
                     Job:TestJob()
                     {
                         SByteCollection:myCollection = SByte.Range(10, 20, 2);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
                Step = (WitConstantNumeric)"2",
            }.WithReturnReference("myCollection")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableSByteCollection("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         myCollection = SByte.Range(10, 20);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
            }.WithReturnReference("myCollection")));

            script = """
                     Job:TestJob()
                     {
                         SByte.Range(10, 20);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivitySByteRange
            {
                From = (WitConstantNumeric)"10",
                To = (WitConstantNumeric)"20",
            }));
        }
        
        [Test]
        public void ParseActivityWrongParametersTest()
        {

            var script = """
                     Job:TestJob()
                     {
                         SByteCollection:myCollection = SByte.Range(10);
                     }
                     """;


            Assert.Throws<WitEngineActivityParsingException<WitActivitySByteRange>>(() => WitEngineSdk.Instance.Compile(script));
            
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             SByteCollection:myCollection = SByte.Range(10, 20);
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"].Value, Is.EqualTo(new sbyte[] {10, 11, 12, 13, 14, 15, 16, 17, 18, 19}));

            script = """
                     Job:TestJob()
                     {
                         SByteCollection:myCollection = SByte.Range(10, 20, 2);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection"].Value, Is.EqualTo(new sbyte[] {10, 12, 14, 16, 18}));
            
            script = """
                     Job:TestJob()
                     {
                         SByteCollection:myCollection1 = SByte.Range(10, 20);
                         SByteCollection:myCollection2 = myCollection1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myCollection1"].Value, Is.EqualTo(new sbyte[] {10, 11, 12, 13, 14, 15, 16, 17, 18, 19}));
            Assert.That(job.Variables["myCollection2"].Value, Is.EqualTo(new sbyte[] {10, 11, 12, 13, 14, 15, 16, 17, 18, 19}));
        }
        
    }
}
