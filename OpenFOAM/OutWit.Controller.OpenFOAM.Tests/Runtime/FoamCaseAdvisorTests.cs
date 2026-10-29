using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

/// <summary>
/// The node reads the files the advice needs from its blob cache - the
/// setFieldsDict, the initial fields and the files they include, tokens in
/// place - and says what the Model says.
/// </summary>
[TestFixture]
public class FoamCaseAdvisorTests
{
    private string m_storage = null!;

    private FoamTestBlobService m_blobs = null!;

    [SetUp]
    public void Setup()
    {
        m_storage = OpenFOAMTestPaths.CreateScratch("foam-advice");
        m_blobs = new FoamTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_storage);
    }

    #region Tools

    private FoamFileRefData BlobFile(string relativePath, string text, bool templated = false)
    {
        return m_blobs.AddFile(relativePath, text, templated);
    }

    private FoamTaskData Task(int threads, bool parallel, bool setFields)
    {
        var steps = new List<FoamStepData> { new() { Utility = "blockMesh" } };
        if (setFields)
            steps.Add(new FoamStepData { Utility = "setFields" });
        steps.Add(new FoamStepData { Utility = "interFoam", Parallel = parallel });

        return new FoamTaskData
        {
            Case = new FoamCaseData
            {
                Threads = threads,
                Recipe = new FoamRecipeData { Application = "interFoam", Steps = steps },
                BaseFiles =
                [
                    BlobFile("system/controlDict", "application interFoam;\n"),
                    BlobFile("system/setFieldsDict", "defaultFieldValues ( volScalarFieldValue alpha.water 0 );\nregions ();\n"),
                    BlobFile("0/alpha.water", "internalField uniform {{oc1}};\n", templated: true),
                    BlobFile("0/U", "internalField uniform ({{oc2}} 0 0);\n", templated: true)
                ]
            },
            Substitutions = [new FoamTokenValueData { Token = "{{oc1}}", Value = "0.5" }, new FoamTokenValueData { Token = "{{oc2}}", Value = "1" }]
        };
    }

    #endregion

    #region Advice Tests

    [Test]
    public async Task TheAdviceNamesTheRanksAndTheOverwrittenFieldTest()
    {
        var advice = await FoamCaseAdvisor.AdviseAsync(Task(threads: 4, parallel: false, setFields: true), m_blobs);

        Assert.That(advice, Has.Count.EqualTo(2));
        Assert.That(advice[0].Text, Does.StartWith("The task asks for 4 ranks, and no step of the recipe runs in parallel"));
        Assert.That(advice[0].Kind, Is.EqualTo(FoamNoteKind.Info), "the values stand, obtained on one rank");
        Assert.That(advice[1].Text, Is.EqualTo("0/alpha.water: internalField carries {{oc1}}, which the study sweeps, and the recipe's setFields step overwrites it everywhere (system/setFieldsDict defaultFieldValues names alpha.water): the swept value has no effect."));
        Assert.That(advice[1].Kind, Is.EqualTo(FoamNoteKind.Warning), "the swept value does not reach the solve");
        Assert.That(advice.Select(note => note.Column), Is.All.Null, "advice is about the variant, not a response column");
    }

    [Test]
    public async Task AFieldThatReachesASweptIncludedValueIsReadWithItsIncludeTest()
    {
        // motorBike's shape: U carries no token and is not templated; the
        // include it reaches through $flowVelocity is.
        var task = Task(threads: 1, parallel: false, setFields: true);
        task.Case!.BaseFiles.RemoveAll(file => file.RelativePath.StartsWith("0/", StringComparison.Ordinal) || file.RelativePath == "system/setFieldsDict");
        task.Case.BaseFiles.Add(BlobFile("system/setFieldsDict", "defaultFieldValues ( volVectorFieldValue U (0 0 0) );\nregions ();\n"));
        task.Case.BaseFiles.Add(BlobFile("0/U", "#include \"include/initialConditions\"\ninternalField uniform $flowVelocity;\n"));
        task.Case.BaseFiles.Add(BlobFile("0/include/initialConditions", "flowVelocity ({{oc2}} 0 0);\n", templated: true));

        var advice = await FoamCaseAdvisor.AdviseAsync(task, m_blobs);

        Assert.That(advice.Select(note => note.Text), Is.EqualTo(new[]
        {
            "0/U: internalField carries {{oc2}} through $flowVelocity of 0/include/initialConditions, which the study sweeps, and the recipe's setFields step overwrites it everywhere (system/setFieldsDict defaultFieldValues names U): the swept value has no effect."
        }));
    }

    [Test]
    public async Task ATaskThatDoesWhatItMeansGetsNoAdviceTest()
    {
        Assert.That(await FoamCaseAdvisor.AdviseAsync(Task(threads: 4, parallel: true, setFields: false), m_blobs), Is.Empty);
        Assert.That(await FoamCaseAdvisor.AdviseAsync(Task(threads: 1, parallel: false, setFields: false), m_blobs), Is.Empty, "no setFields step: the dictionary is not even read");
    }

    #endregion
}
