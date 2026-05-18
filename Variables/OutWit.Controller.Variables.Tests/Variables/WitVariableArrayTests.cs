using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.Arrays;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableArrayTests
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
                (WitConstantString)"text", 
                (WitConstantNumeric)"23", 
                (WitConstantBoolean)"true"
            };
            var variable = new WitVariableArray("myArray");
            Assert.That(variable.Name, Is.EqualTo("myArray"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("Array:myArray;"));

            variable = new WitVariableArray("myArray", array.Clone());
            Assert.That(variable.Name, Is.EqualTo("myArray"));
            Assert.That(variable.Value, Was.EqualTo(array.Clone()));
            Assert.That(variable.ToString(), Is.EqualTo("Array:myArray = [\"text\", 23, true];"));
        }

        [Test]
        public void IsTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };
            
            var variable = new WitVariableArray("myArray", array.Clone());

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue(WitArray.Empty)));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myArray1")));
        }

        [Test]
        public void CloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };

            var variable = new WitVariableArray("myArray", array.Clone());

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Was.EqualTo("myArray"));
            Assert.That(clone.Value, Was.EqualTo(array.Clone()));
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var array = (WitArray)new IWitParameter[]
            {
                (WitConstantString)"text",
                (WitConstantNumeric)"23",
                (WitConstantBoolean)"true"
            };

            var variable = new WitVariableArray("myArray", array.MemoryPackClone());

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Was.EqualTo("myArray"));
            Assert.That(clone.Value, Was.EqualTo(array.Clone()));
        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Array:myArray;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableArray("myArray")));
        }
    }
}