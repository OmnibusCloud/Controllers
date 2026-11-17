using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The parser accepts exactly what the published schema allows (audit finding V:M-4): a field
/// the schema does not have, a field given twice, a value of another type, a number outside
/// its range, a NUL in a program or an argument and text that is not Unicode are each refused
/// by name, so a misspelt key can never turn a test into a pass. Every case here was accepted
/// (or silently changed) before 0.14.0.
/// </summary>
[TestFixture]
public sealed class VerifyTasksetSchemaTests
{
    #region Constants

    private const string HEAD = """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}""";

    #endregion

    #region Unknown Fields

    [Test]
    public void M4AMisspeltExpectationIsRefusedAndNeverAPassTest()
    {
        // The audit's input: the program prints 1, the case was meant to expect 2, and the key
        // is misspelt. The expectation was dropped and the task passed.
        var parsed = VerifyTasksetParser.Parse(HEAD + ""","suite":[{"expected_stdoutt":"2\n"}]}""");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty, "a task with a field the schema does not have is not run");
            Assert.That(parsed.Errors, Has.Count.EqualTo(1));
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: unknown field 'suite[0].expected_stdoutt'"));
        });
    }

    [TestCase(""","limitz":{"fuel":5}}""", "unknown field 'limitz'")]
    [TestCase(""","limits":{"fule":5}}""", "unknown field 'limits.fule'")]
    [TestCase(""","suite":[{},{"stdinn":"x"}]}""", "unknown field 'suite[1].stdinn'")]
    [TestCase(""","expected_stdout":"1\n"}""", "unknown field 'expected_stdout'")]
    [TestCase(""","Runtime":"python-3.14.6"}""", "unknown field 'Runtime'")]
    [TestCase(""","timeout":5}""", "unknown field 'timeout'")]
    public void M4AFieldTheSchemaDoesNotHaveIsRefusedByNameTest(string tail, string expected)
    {
        var parsed = VerifyTasksetParser.Parse(HEAD + tail);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [Test]
    public void M4AnUnknownFieldIsShownInPlainCharactersAndBoundedTest()
    {
        // The name is the lab's to write: what travels back is cut and has no control characters.
        var parsed = VerifyTasksetParser.Parse(
            HEAD + ",\"" + new string('x', 5000) + "\\u0007\\n\":1}\n" +
            HEAD + ",\"bell\\u0007 line\\n" + new string('y', 100) + "\":1}");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors, Has.Count.EqualTo(2));
            Assert.That(parsed.Errors.ElementAtOrDefault(0), Is.EqualTo("line 1: unknown field in 'the task' (its name is longer than 256 bytes)"));
            Assert.That(parsed.Errors.ElementAtOrDefault(1), Is.EqualTo("line 2: unknown field 'bell??line?" + new string('y', 53) + "'"), "cut to 64 characters, anything but plain characters as '?'");
            Assert.That(string.Concat(parsed.Errors).Any(char.IsControl), Is.False, "no control character travels back");
        });
    }

    #endregion

    #region Shapes And Types

    [TestCase(""","suite":{"stdin":"x"}}""", "'suite' must be an array of cases")]
    [TestCase(""","suite":"none"}""", "'suite' must be an array of cases")]
    [TestCase(""","suite":[1]}""", "'suite[0]' must be an object")]
    [TestCase(""","suite":[{},["x"]]}""", "'suite[1]' must be an object")]
    public void M4ASuiteOfTheWrongShapeIsRefusedTest(string tail, string expected)
    {
        // An object in place of the array was dropped without a word: the task ran without its tests.
        var parsed = VerifyTasksetParser.Parse(HEAD + tail);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [TestCase(""","stdin":7}""", "'stdin' must be a string")]
    [TestCase(""","stdin":null}""", "'stdin' must be a string")]
    [TestCase(""","args":"x"}""", "'args' must be an array of strings")]
    [TestCase(""","args":["a",2]}""", "'args[1]' must be a string")]
    [TestCase(""","seed":"s"}""", "'seed' must be an integer")]
    [TestCase(""","seed":1.5}""", "'seed' must be an integer")]
    [TestCase(""","index":"3"}""", "'index' must be an integer")]
    [TestCase(""","entry":5}""", "'entry' must be a string")]
    [TestCase(""","limits":[]}""", "'limits' must be an object")]
    [TestCase(""","limits":{"fuel":"many"}}""", "'limits.fuel' must be an integer")]
    [TestCase(""","limits":{"memory":2.5}}""", "'limits.memory' must be an integer")]
    [TestCase(""","suite":[{"expected_exit":"0"}]}""", "'suite[0].expected_exit' must be an integer")]
    [TestCase(""","suite":[{"expected_stdout":5}]}""", "'suite[0].expected_stdout' must be a string")]
    [TestCase(""","suite":[{"args":"x"}]}""", "'suite[0].args' must be an array of strings")]
    public void M4AValueOfAnotherTypeIsRefusedByNameTest(string tail, string expected)
    {
        // Each of these was ignored: the field took its default and the preflight said WellFormed.
        var parsed = VerifyTasksetParser.Parse(HEAD + tail);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [TestCase("""{"runtime":"python-3.14.6","sources":["m.py"]}""", "'sources' must be an object of file name and content")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":null}}""", "'a source' must be a string")]
    [TestCase("""{"runtime":7,"sources":{"m.py":"print(1)"}}""", "'runtime' must be a string")]
    [TestCase("""["python-3.14.6"]""", "a task must be one JSON object")]
    [TestCase("\"task\"", "a task must be one JSON object")]
    public void M4ATaskOfAnotherShapeIsRefusedByNameTest(string line, string expected)
    {
        var parsed = VerifyTasksetParser.Parse(line);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [TestCase(""","limits":{"fuel":0}}""", "'limits.fuel' must be 1 or more")]
    [TestCase(""","limits":{"memory":-5}}""", "'limits.memory' must be 1 or more")]
    [TestCase(""","limits":{"stdout_bytes":0}}""", "'limits.stdout_bytes' must be 1 or more")]
    [TestCase(""","limits":{"stderr_bytes":3000000000}}""", "'limits.stderr_bytes' must be 2147483647 or less")]
    [TestCase(""","limits":{"wall_ms":0}}""", "'limits.wall_ms' must be 1 or more")]
    [TestCase(""","index":-1}""", "'index' must be 0 or more")]
    [TestCase(""","index":2147483648}""", "'index' must be 2147483647 or less")]
    [TestCase(""","seed":9223372036854775808}""", "'seed' must be 9223372036854775807 or less")]
    [TestCase(""","suite":[{"expected_exit":4294967296}]}""", "'suite[0].expected_exit' must be 2147483647 or less")]
    public void M4ANumberOutsideItsRangeIsRefusedByNameTest(string tail, string expected)
    {
        // The schema says a limit is 1 or more; 0 and negative numbers used to mean "the default".
        var parsed = VerifyTasksetParser.Parse(HEAD + tail);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [Test]
    public void M4AnIntegerMayBeWrittenAnyWayTheSchemaAllowsTest()
    {
        // To a schema 1.0 and 2e0 are integers. The parser took them for errors with the
        // library's own words; "exactly what the schema allows" cuts both ways.
        var parsed = VerifyTasksetParser.Parse(HEAD + ""","index":2e0,"seed":41.0,"limits":{"fuel":5e9}}""");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.Tasks, Has.Count.EqualTo(1));
            Assert.That(parsed.Tasks.FirstOrDefault()?.TaskIndex, Is.EqualTo(2));
            Assert.That(parsed.Tasks.FirstOrDefault()?.RandomSeed, Is.EqualTo(41));
            Assert.That(parsed.Tasks.FirstOrDefault()?.Limits?.FuelBudget, Is.EqualTo(5_000_000_000));
        });
    }

    #endregion

    #region Duplicates

    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"sources":{"m.py":"print(2)"}}""", "field 'sources' is given twice")]
    [TestCase("""{"runtime":"python-3.14.6","runtime":"quickjs-0.15.1","sources":{"m.py":"print(1)"}}""", "field 'runtime' is given twice")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)","m.py":"print(2)"}}""", "source name 'm.py' is given twice")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"suite":[],"suite":[{"expected_stdout":"1\n"}]}""", "field 'suite' is given twice")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"limits":{"fuel":5,"fuel":6}}""", "field 'limits.fuel' is given twice")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"suite":[{"stdin":"a","stdin":"b"}]}""", "field 'suite[0].stdin' is given twice")]
    public void M4ANameGivenTwiceIsRefusedTest(string line, string expected)
    {
        // Two sources of one name were both kept and the first ran, where a JSON library keeps
        // the last: the task meant one thing to its author and another to the node.
        var parsed = VerifyTasksetParser.Parse(line);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    #endregion

    #region Text

    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)\u0000print(2)"}}""", "'a source' holds a NUL character")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"args":["a\u0000b"]}""", "'args[0]' holds a NUL character")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"suite":[{"args":["x","\u0000"]}]}""", "'suite[0].args[1]' holds a NUL character")]
    [TestCase("""{"runtime":"python-3.14.6","entry":"m.py\u0000","sources":{"m.py":"print(1)"}}""", "'entry' holds a NUL character")]
    public void M4ANulInAProgramOrAnArgumentIsRefusedTest(string line, string expected)
    {
        // A program is handed to its interpreter as a C string: print(1)\0print(2) printed 1.
        var parsed = VerifyTasksetParser.Parse(line);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [Test]
    public void M4ANulInStdinOrInAnExpectedOutputIsDataTest()
    {
        // What a program reads and prints is bytes, and a NUL is one of them.
        var parsed = VerifyTasksetParser.Parse(HEAD + ""","stdin":"a\u0000b","suite":[{"stdin":"\u0000","expected_stdout":"x\u0000y"}]}""");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.Tasks.FirstOrDefault()?.Stdin, Is.EqualTo("a\0b"));
            Assert.That(parsed.Tasks.FirstOrDefault()?.Suite?.Cases[0].ExpectedStdout, Is.EqualTo("x\0y"));
        });
    }

    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print('\ud800')"}}""", "'a source' is not valid Unicode text")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"stdin":"\udc00x"}""", "'stdin' is not valid Unicode text")]
    public void M4TextThatIsNotUnicodeIsRefusedByNameTest(string line, string expected)
    {
        // Half of a surrogate pair cannot be written as UTF-8: the program a node ran was not
        // the program the lab wrote. It was refused before, in the JSON library's own words.
        var parsed = VerifyTasksetParser.Parse(line);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors.FirstOrDefault(), Is.EqualTo("line 1: " + expected));
        });
    }

    [Test]
    public void M4ALineThatIsNotJsonSaysSoInFixedWordsTest()
    {
        var parsed = VerifyTasksetParser.Parse(string.Join("\n", "not json at all", HEAD + "} trailing", HEAD + ","));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors, Has.Count.EqualTo(3));
            Assert.That(parsed.Errors, Has.All.Match(@"^line \d: not valid JSON \(at byte \d+\)$"));
        });
    }

    #endregion

    #region Runtime Ids

    [TestCase("PYTHON-3.14.6")]
    [TestCase("Python-3.14.6")]
    [TestCase("WASI")]
    public void M4ARuntimeIdIsMatchedToTheLetterTest(string runtime)
    {
        // The catalog found an id whatever its case, so two spellings of one runtime were two
        // batches under two names.
        Assert.That(OutWit.Controller.AI.Verify.Runtimes.VerifyRuntimeCatalog.Find(runtime), Is.Null);
    }

    #endregion

    #region The Checker

    [TestCase("""{"runtime":"python-3.14.6","sources":{"c.py":"print('OK')"},"expected_stdoutt":"OK\n"}""", "unknown field 'expected_stdoutt'")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"c.py":"print('OK')"},"suite":[{"expected_stdout":"OK\n"}]}""", "unknown field 'suite'")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"c.py":"print('OK')"},"index":3}""", "unknown field 'index'")]
    [TestCase("""{"runtime":"python-3.14.6","sources":{"c.py":"print('OK')"},"expected_exit":"0"}""", "'expected_exit' must be an integer")]
    public void M4ACheckerIsHeldToItsSchemaTest(string json, string expected)
    {
        // A misspelt rule left the checker with "exit code 0 only": every candidate passed.
        var refused = Assert.Throws<FormatException>(() => VerifyCheckerSpec.Parse(json));

        Assert.That(refused?.Message, Does.EndWith(expected));
    }

    #endregion
}
