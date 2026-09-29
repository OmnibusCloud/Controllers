using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

/// <summary>
/// The controller's include: one line, <c>#includeFunc &lt;response&gt;</c>, at
/// the end of the top-level <c>functions</c> of the node's copy of
/// <c>system/controlDict</c> - never in a comment, a string or a nested
/// dictionary that happens to say <c>functions</c> - and a log that says what
/// was added and why.
/// </summary>
[TestFixture]
public class FoamSolveFunctionsTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-solve-functions");
        Directory.CreateDirectory(Path.Combine(m_case, "system"));
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Text Tests

    [Test]
    public void TheLineEndsTheCasesOwnFunctionsTest()
    {
        const string text =
            "application simpleFoam;\n" +
            "endTime 100;\n" +
            "\n" +
            "functions\n" +
            "{\n" +
            "    // the case's own\n" +
            "    forces1 { type forces; libs (forces); patches (innerWall); }\n" +
            "}\n" +
            "\n" +
            "// ************************************************************************* //\n";

        var (edited, line, refusal) = FoamSolveFunctions.AddInclude(text, "torque_sweep");

        Assert.That(refusal, Is.Null);
        Assert.That(edited, Is.EqualTo(
            "application simpleFoam;\n" +
            "endTime 100;\n" +
            "\n" +
            "functions\n" +
            "{\n" +
            "    // the case's own\n" +
            "    forces1 { type forces; libs (forces); patches (innerWall); }\n" +
            "    #includeFunc torque_sweep\n" +
            "}\n" +
            "\n" +
            "// ************************************************************************* //\n"));
        Assert.That(line, Is.EqualTo(8));
    }

    [Test]
    public void ACaseWithoutFunctionsGetsABlockOfItsOwnTest()
    {
        const string text = "application simpleFoam;\nendTime 100;\n";

        var (edited, line, refusal) = FoamSolveFunctions.AddInclude(text, "load");

        Assert.That(refusal, Is.Null);
        Assert.That(edited, Is.EqualTo("application simpleFoam;\nendTime 100;\n\nfunctions\n{\n    #includeFunc load\n}\n"));
        Assert.That(line, Is.EqualTo(6));
    }

    [Test]
    public void OnlyTheTopLevelKeywordIsTheFunctionsTest()
    {
        const string text =
            "// functions { a comment }\n" +
            "/* functions\n{ } */\n" +
            "note \"functions { in a string }\";\n" +
            "OptimisationSwitches { functions 1; }\n" +
            "myfunctions { }\n" +
            "application simpleFoam;\n";

        var (edited, _, refusal) = FoamSolveFunctions.AddInclude(text, "load");

        Assert.That(refusal, Is.Null);
        Assert.That(edited, Is.EqualTo(text + "\nfunctions\n{\n    #includeFunc load\n}\n"), "no line lands in a comment, a string or a nested dictionary");
    }

    [Test]
    public void BracesInACodedFunctionAreCodeNotTheBlockTest()
    {
        const string text =
            "functions\n" +
            "{\n" +
            "    coded { type coded; codeExecute #{ if (on) { Info<< \"}\" << endl; } #}; }\n" +
            "}\n" +
            "application simpleFoam;\n";

        var (edited, line, refusal) = FoamSolveFunctions.AddInclude(text, "load");

        Assert.That(refusal, Is.Null);
        Assert.That(edited, Is.EqualTo(text.Replace("#}; }\n}\n", "#}; }\n    #includeFunc load\n}\n")));
        Assert.That(line, Is.EqualTo(4));
    }

    [Test]
    public void AnEmptyBlockOnOneLineOpensTest()
    {
        var (edited, line, _) = FoamSolveFunctions.AddInclude("application simpleFoam;\nfunctions {}\n", "load");

        Assert.That(edited, Is.EqualTo("application simpleFoam;\nfunctions {\n    #includeFunc load\n}\n"));
        Assert.That(line, Is.EqualTo(3));
    }

    [Test]
    public void TheLastOfTwoBlocksIsTheOneOpenFoamReadsTest()
    {
        const string text = "functions { a { type probes; } }\napplication simpleFoam;\nfunctions\n{\n}\n";

        var (edited, line, _) = FoamSolveFunctions.AddInclude(text, "load");

        Assert.That(edited, Is.EqualTo("functions { a { type probes; } }\napplication simpleFoam;\nfunctions\n{\n    #includeFunc load\n}\n"));
        Assert.That(line, Is.EqualTo(5));
    }

    [Test]
    public void TheFilesLineEndingsAreKeptTest()
    {
        var (edited, _, _) = FoamSolveFunctions.AddInclude("application simpleFoam;\r\nfunctions\r\n{\r\n}\r\n", "load");

        Assert.That(edited, Is.EqualTo("application simpleFoam;\r\nfunctions\r\n{\r\n    #includeFunc load\r\n}\r\n"));
    }

    [Test]
    public void FunctionsThatAreNotABlockAreRefusedNotRewrittenTest()
    {
        var (edited, _, refusal) = FoamSolveFunctions.AddInclude("application simpleFoam;\nfunctions #includeEtc \"caseDicts/functions\";\n", "load");

        Assert.That(edited, Is.Null);
        Assert.That(refusal, Is.EqualTo(
            "system/controlDict:2: 'functions' is not a block the controller can add a line to; " +
            "write it as functions { ... } for a force to be measured during the solve."));
    }

    #endregion

    #region File Tests

    [Test]
    public void TheNodesCopyGainsTheLineAndTheLogSaysWhatAndWhyTest()
    {
        var controlDict = Path.Combine(m_case, "system", "controlDict");
        File.WriteAllText(controlDict, "application simpleFoam;\nfunctions\n{\n}\n");
        var log = Path.Combine(m_case, "log.includeFunc");

        var outcome = FoamSolveFunctions.Include(m_case, "torque_sweep", log);

        Assert.That(outcome.ExitCode, Is.EqualTo(0), outcome.LogTail);
        Assert.That(File.ReadAllText(controlDict), Is.EqualTo("application simpleFoam;\nfunctions\n{\n    #includeFunc torque_sweep\n}\n"));
        var text = File.ReadAllText(log);
        Assert.That(text, Does.Contain("#includeFunc torque_sweep").And.Contain("system/controlDict:4"));
        Assert.That(text, Does.Contain("node's copy").And.Contain("the case's own files are not changed"));
        Assert.That(text, Does.Contain("rotating zone (MRF)").And.Contain("-postProcess"), "the reason, in the log that travels with the variant");
    }

    [Test]
    public void ARefusedIncludeLeavesTheCopyAsItWasAndFailsTheStepTest()
    {
        var controlDict = Path.Combine(m_case, "system", "controlDict");
        const string original = "application simpleFoam;\nfunctions #includeEtc \"caseDicts/functions\";\n";
        File.WriteAllText(controlDict, original);
        var log = Path.Combine(m_case, "log.includeFunc");

        var outcome = FoamSolveFunctions.Include(m_case, "load", log);

        Assert.That(outcome.ExitCode, Is.Not.EqualTo(0));
        Assert.That(File.ReadAllText(controlDict), Is.EqualTo(original));
        Assert.That(outcome.LogTail, Does.Contain("'functions' is not a block"));
    }

    [Test]
    public void ACaseWithoutAControlDictFailsTheStepByNameTest()
    {
        var outcome = FoamSolveFunctions.Include(m_case, "load", Path.Combine(m_case, "log.includeFunc"));

        Assert.That(outcome.ExitCode, Is.Not.EqualTo(0));
        Assert.That(outcome.LogTail, Does.Contain("system/controlDict"));
    }

    #endregion
}
