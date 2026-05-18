using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Model;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.References;

namespace OutWit.Controller.Variables.Tests.Activities
{
    [TestFixture]
    public class WitActivityColorTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var activity = new WitActivityColor();
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Red, Was.EqualTo(null));
            Assert.That(activity.Green, Was.EqualTo(null));
            Assert.That(activity.Blue, Was.EqualTo(null));
            Assert.That(activity.Alpha, Was.EqualTo(null));

            activity = new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo((WitConstantString)"#3366AA"));
            Assert.That(activity.Red, Was.EqualTo(null));
            Assert.That(activity.Green, Was.EqualTo(null));
            Assert.That(activity.Blue, Was.EqualTo(null));
            Assert.That(activity.Alpha, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Color(\"#3366AA\");"));


            activity = new WitActivityColor
            {
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Red, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Green, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Blue, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Alpha, Was.EqualTo(null));
            Assert.That(activity.ToString(), Is.EqualTo("Color(10, 20, 30);"));

            activity = new WitActivityColor
            {
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40",
            };
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(activity.Value, Was.EqualTo(null));
            Assert.That(activity.Red, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(activity.Green, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(activity.Blue, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(activity.Alpha, Was.EqualTo((WitConstantNumeric)"40"));
            Assert.That(activity.ToString(), Is.EqualTo("Color(10, 20, 30, 40);"));

            activity.SetReturnReference("reference");
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.EqualTo("reference"));
            Assert.That(activity.ToString(), Is.EqualTo("reference = Color(10, 20, 30, 40);"));
        }

        [Test]
        public void IsTest()
        {
            var activity = new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA",
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40"
            }.WithReturnReference("reference");

            Assert.That(activity, Was.EqualTo(activity.Clone()));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.StagesCount, 2)));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.ReturnReference, "reference1")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Value, (WitConstantString)"#4366AA")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Red, (WitConstantNumeric)"11")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Green, (WitConstantNumeric)"21")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Blue, (WitConstantNumeric)"31")));
            Assert.That(activity, Was.Not.EqualTo(activity.With(x => x.Alpha, (WitConstantNumeric)"41")));
        }

        [Test]
        public void CloneTest()
        {
            var activity = new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA",
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40"
            }.WithReturnReference("reference");

            var clone = activity.Clone() as WitActivityColor;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"#3366AA"));
            Assert.That(clone.Red, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Green, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(clone.Blue, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(clone.Alpha, Was.EqualTo((WitConstantNumeric)"40"));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var activity = new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA",
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40"
            }.WithReturnReference("reference");

            var clone = activity.MemoryPackClone() as WitActivityColor;

            Assert.That(clone, Was.EqualTo(activity));
            Assert.That(clone.StagesCount, Is.EqualTo(1));
            Assert.That(clone.ReturnReference, Is.EqualTo("reference"));
            Assert.That(clone.Value, Was.EqualTo((WitConstantString)"#3366AA"));
            Assert.That(clone.Red, Was.EqualTo((WitConstantNumeric)"10"));
            Assert.That(clone.Green, Was.EqualTo((WitConstantNumeric)"20"));
            Assert.That(clone.Blue, Was.EqualTo((WitConstantNumeric)"30"));
            Assert.That(clone.Alpha, Was.EqualTo((WitConstantNumeric)"40"));
        }

        [Test]
        public void ParseActivityTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Color:myColor = Color("#3366AA");
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableColor("myColor")));

            script = """
                     Job:TestJob()
                     {
                         Color:myColor = "#3366AA";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableColor("myColor")));

            script = """
                     Job:TestJob()
                     {
                         myColor = Color("#3366AA");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor")));

            script = """
                     Job:TestJob()
                     {
                         Color("#3366AA");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }));

            script = """
                         Job:TestJob()
                         {
                             Color:myColor1 = "#3366AA";
                             Color:myColor2 = myColor1;
                         }
                         """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Variables["myColor1"], Was.EqualTo(new WitVariableColor("myColor1")));
            Assert.That(job.Variables["myColor2"], Was.EqualTo(new WitVariableColor("myColor2")));
            Assert.That(job.Activities[0], Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor1")));
            Assert.That(job.Activities[1], Was.EqualTo(new WitActivityColor
            {
                Value = (WitReference)"myColor1"
            }.WithReturnReference("myColor2")));
            
            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color(10, 20, 30);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
            }.WithReturnReference("myColor")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableColor("myColor")));
            
            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color(10, 20, 30, 40);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40",
            }.WithReturnReference("myColor")));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableColor("myColor")));

            script = """
                     Job:TestJob()
                     {
                         myColor = Color(10, 20, 30, 40);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40",
            }.WithReturnReference("myColor")));


            script = """
                     Job:TestJob()
                     {
                         Color(10, 20, 30, 40);
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(0));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Red = (WitConstantNumeric)"10",
                Green = (WitConstantNumeric)"20",
                Blue = (WitConstantNumeric)"30",
                Alpha = (WitConstantNumeric)"40",
            }));


            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color("#3366AA");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor")));

            script = """
                     Job:TestJob()
                     {
                        Color:myColor;
                        myColor = Color("#3366AA");
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor")));

            script = """
                     Job:TestJob()
                     {
                        Color:myColor;
                        
                        myColor = "#3366AA";
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitConstantString)"#3366AA"
            }.WithReturnReference("myColor")));

            script = """
                     Job:TestJob()
                     {
                        Color:myColor1 = "#3366AA";
                        Color:myColor2;
                        
                        myColor2 = myColor1;
                     }
                     """;

            job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(2));
            Assert.That(job.Variables.Count, Is.EqualTo(2));
            Assert.That(job.Activities.Last(), Was.EqualTo(new WitActivityColor
            {
                Value = (WitReference)"myColor1"
            }.WithReturnReference("myColor2")));
        }

        [Test]
        public void ParseActivityWrongParametersTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Color:myColor = Color(#3366AA);
                         }
                         """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<IWitActivity>>());
            
            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color(10, 20);
                     }
                     """;
            
            Assert.That(()=>WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityColor>>());

            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color(10);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityColor>>());

            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color(10, 20, 30, 40, 50);
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityColor>>());


            script = """
                     Job:TestJob()
                     {
                         Color:myColor = Color(10, 20, "30");
                     }
                     """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script), Throws.InstanceOf<WitEngineActivityParsingException<WitActivityColor>>());
        }

        [Test]
        public void ProcessingTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Color:myColor = Color("#3366AA");
                         }
                         """;
            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);

            var resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170)));

            script = """
                     Job:TestJob()
                     {
                         Color:myColor = "#3366AA";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170)));

            script = """
                     Job:TestJob()
                     {
                         Color:myColor = "#F03366AA";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170, 240)));

            script = """
                     Job:TestJob()
                     {
                         Color:myColor1 = "#3366AA";
                         Color:myColor2 = myColor1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor1"].Value, Was.EqualTo(new WitColor(51, 102, 170)));
            Assert.That(job.Variables["myColor2"].Value, Was.EqualTo(new WitColor(51, 102, 170)));
            
            script = """
                         Job:TestJob()
                         {
                             Color:myColor = Color(51, 102, 170);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);
            
            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();
            
            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170)));

            script = """
                         Job:TestJob()
                         {
                             Color:myColor = Color(51, 102, 170, 240);
                         }
                         """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170, 240)));
            
            script = """
                     Job:TestJob()
                     {
                        Byte:red = 51;
                        Byte:blue = 170;
                     
                        Color:myColor = Color(red, 102, blue);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170)));

            script = """
                     Job:TestJob()
                     {
                         Color(51, 102, 170, 240);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));

            script = """
                     Job:TestJob()
                     {
                        Color:myColor;
                        myColor = Color("#3366AA");
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170)));


            script = """
                     Job:TestJob()
                     {
                        Color:myColor;
                        myColor = "#3366AA";
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor"].Value, Was.EqualTo(new WitColor(51, 102, 170)));

            script = """
                     Job:TestJob()
                     {
                        Color:myColor1 = "#3366AA";
                        Color:myColor2;
                        
                        myColor2 = myColor1;
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(job.Variables["myColor2"].Value, Was.EqualTo(new WitColor(51, 102, 170)));
        }

        [Test]
        public void ProcessingFailTest()
        {
            var script = """
                         Job:TestJob()
                         {
                             Color:myColor = Color(51, 102, 300);
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

            script = """
                     Job:TestJob()
                     {
                        String:red = "51";
                        Byte:blue = 170;
                     
                        Color:myColor = Color(red, 102, blue);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);

            script = """
                     Job:TestJob()
                     {
                        Int:red = 51;
                        Byte:blue = 170;
                     
                        Color:myColor = Color(red, 102, blue);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
            
            script = """
                     Job:TestJob()
                     {
                        myColor = Color(51, 102, 170);
                     }
                     """;
            job = WitEngineSdk.Instance.Compile(script);
            task = WitEngineSdk.Instance.ScheduleProcessing(job);

            resetEvent = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { resetEvent.Set(); };
            task.Run();
            resetEvent.WaitOne();

            Assert.That(task.Status?.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(task.Status.Message, Is.Not.Null);
            Console.WriteLine(task.Status.Message);
        }
    }
}
