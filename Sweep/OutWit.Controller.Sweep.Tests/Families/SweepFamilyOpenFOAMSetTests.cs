using System.Reflection;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.Sweep.Families;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;

namespace OutWit.Controller.Sweep.Tests.Families;

[TestFixture]
public class SweepFamilyOpenFOAMSetTests
{
    private string m_storage = null!;
    private SweepTestBlobService m_blobs = null!;

    [SetUp]
    public void Setup()
    {
        m_storage = Path.Combine(Path.GetTempPath(), $"sweep-foam-set-{Guid.NewGuid():N}");
        m_blobs = new SweepTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storage))
            Directory.Delete(m_storage, recursive: true);
    }

    #region Tools

    private static FoamFileRefData File(string relativePath, bool templated = false)
    {
        return new FoamFileRefData { RelativePath = relativePath, BlobId = Guid.NewGuid(), Templated = templated };
    }

    private static SweepOpenFOAMCaseData Case(int variantIndex, string name, long cells = 0)
    {
        return new SweepOpenFOAMCaseData
        {
            VariantIndex = variantIndex,
            Name = name,
            BaseFiles = [File("system/controlDict"), File("system/blockMeshDict"), File("0/U")],
            Recipe = new FoamRecipeData { Application = "icoFoam", Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "icoFoam" }] },
            CellCount = cells,
            SolverClass = cells > 0 ? "incompressible-transient" : string.Empty
        };
    }

    private static SweepOptionsData Study(int variants)
    {
        return new SweepOptionsData
        {
            Variants = Enumerable.Range(0, variants).Select(index => new SweepVariantData { VariantIndex = index }).ToList(),
            OpenFOAM = new FoamCaseData
            {
                Threads = 2,
                Extraction = new FoamExtractionRequestData { Responses = [new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"] }] },
                ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Logs = true },
                CellCount = 900,
                SolverClass = "incompressible-steady",
                TimeBudgetSeconds = 600
            }
        };
    }

    private static SweepFamilyOpenFOAMSet Family(params SweepOpenFOAMCaseData[] cases)
    {
        return new SweepFamilyOpenFOAMSet(new SweepOpenFOAMSetData { Cases = [.. cases] });
    }

    #endregion

    #region Selection Tests

    [Test]
    public void APlanWithACaseSetRunsOnTheSetFamilyTest()
    {
        var set = new SweepOpenFOAMSetData { Cases = [Case(0, "coarse")] };

        Assert.That(SweepFamilies.Of(new SweepPlanData { Options = Study(1), OpenFOAMSet = set }), Is.TypeOf<SweepFamilyOpenFOAMSet>());
        Assert.That(SweepFamilies.Of(new SweepPlanData { Options = Study(1) }), Is.TypeOf<SweepFamilyOpenFOAM>());
        Assert.That(SweepFamilies.For(SweepFamily.OpenFOAM, set), Is.TypeOf<SweepFamilyOpenFOAMSet>());

        var family = SweepFamilies.For(SweepFamily.OpenFOAM, set);
        Assert.That((family.Family, family.TaskType, family.ScriptName), Is.EqualTo((SweepFamily.OpenFOAM, typeof(FoamTaskData), "SweepOpenFOAMSet")));
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task ASetWithACaseForEveryVariantIsAcceptedTest()
    {
        var family = Family(Case(0, "coarse", 400), Case(1, "fine", 1600));

        Assert.That(await family.ValidateAsync(Study(2), m_blobs), Is.Empty);
    }

    [Test]
    public async Task TheStudyBlockOfASetCarriesOnlyWhatTheCasesShareTest()
    {
        var options = Study(1);
        options.OpenFOAM!.BaseFiles = [File("system/controlDict")];
        options.OpenFOAM.Recipe = new FoamRecipeData { Application = "icoFoam", Steps = [new FoamStepData { Utility = "icoFoam" }] };
        options.Parameters = [new SweepParameterData { Name = "U", Token = "{{oc1}}" }];

        var findings = await Family(Case(0, "coarse")).ValidateAsync(options, m_blobs);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "A case set carries every case's files in the set; the study's OpenFOAM block names none.",
            "A case set carries every case's recipe in the set; the study's OpenFOAM block names none.",
            "A case set declares no parameters: every case runs as it is."
        }));
    }

    [Test]
    public async Task EveryVariantHasExactlyOneCaseTest()
    {
        Assert.That(await Family(Case(0, "a")).ValidateAsync(Study(2), m_blobs), Is.EqualTo(new[] { "Variant #1 has no case in the case set." }));
        Assert.That(await Family(Case(0, "a"), Case(1, "b"), Case(1, "c")).ValidateAsync(Study(2), m_blobs), Is.EqualTo(new[] { "Variant #1 has 2 cases in the case set." }));
        Assert.That(await Family(Case(0, "a"), Case(1, "b"), Case(5, "c")).ValidateAsync(Study(2), m_blobs),
            Is.EqualTo(new[] { "The case set carries a case for variant #5, which the variant table does not have." }));
    }

    [Test]
    public async Task EachCaseIsCheckedByTheOpenFOAMRulesUnderItsNameTest()
    {
        var broken = Case(1, "fine");
        broken.BaseFiles.Add(File("processor0/0/U"));
        broken.Recipe!.Steps.Insert(0, new FoamStepData { Utility = "bash" });
        var unnamed = Case(2, string.Empty);
        unnamed.Recipe = null;

        var findings = await Family(Case(0, "coarse"), broken, unnamed).ValidateAsync(Study(3), m_blobs);

        Assert.That(findings, Has.Some.StartsWith("Case 'fine': processor0/0/U:"));
        Assert.That(findings, Has.Some.EqualTo("Case 'fine': Step 1: 'bash' is not on the allow-list."));
        Assert.That(findings, Has.Some.StartsWith("Case #2: "), "a case without a name is named by its variant");
        Assert.That(findings, Has.None.StartsWith("Case 'coarse'"));
    }

    [Test]
    public async Task ACaseOfASetSubstitutesNothingTest()
    {
        var templated = Case(0, "coarse");
        templated.BaseFiles[2].Templated = true;

        var findings = await Family(templated).ValidateAsync(Study(1), m_blobs);

        Assert.That(findings, Is.EqualTo(new[] { "Case 'coarse': 0/U is marked templated, but a case set substitutes nothing." }));
    }

    [Test]
    public async Task AResponseOfASetCarriesNoTokenTest()
    {
        var options = Study(1);
        options.OpenFOAM!.Extraction!.Responses.Add(new FoamResponseSpecData
        {
            Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["lid"],
            Parameters = [new FoamNamedValueData { Name = "magUInf", Value = "{{oc1}}" }]
        });

        var findings = await Family(Case(0, "coarse")).ValidateAsync(options, m_blobs);

        Assert.That(findings, Is.EqualTo(new[] { "Response 'coeffs': token {{oc1}} is not declared by the study." }));
    }

    [Test]
    public async Task ASetWithoutTheStudyBlockIsRefusedTest()
    {
        var options = Study(1);
        options.OpenFOAM = null;

        Assert.That(await Family(Case(0, "coarse")).ValidateAsync(options, m_blobs), Is.EqualTo(new[] { "The study carries no OpenFOAM block." }));
    }

    #endregion

    #region Task Tests

    [Test]
    public async Task EachTaskIsItsOwnCaseWithWhatTheSetSharesTest()
    {
        var options = Study(2);
        var coarse = Case(0, "coarse");
        var fine = Case(1, "fine", 1600);
        var family = Family(fine, coarse);
        var uploadsBefore = Directory.Exists(m_storage) ? Directory.GetFiles(m_storage).Length : 0;

        var tasks = (await family.MakeTasksAsync(options, options.Variants, m_blobs)).Cast<FoamTaskData>().ToList();

        Assert.That(tasks.Select(task => task.VariantIndex), Is.EqualTo(new[] { 0, 1 }), "table order, whatever the set's order");
        Assert.That(tasks.All(task => task.Substitutions.Count == 0), Is.True);

        var block = options.OpenFOAM!;
        var expected = new FoamCaseData
        {
            BaseFiles = fine.BaseFiles,
            Recipe = fine.Recipe,
            Threads = block.Threads,
            Extraction = block.Extraction,
            ArtifactPolicy = block.ArtifactPolicy,
            CellCount = 1600,
            SolverClass = "incompressible-transient",
            TimeBudgetSeconds = block.TimeBudgetSeconds
        };
        Assert.That(tasks[1].Case?.Is(expected), Is.True, "the case's tree, recipe and size; the block's threads, responses, artifacts and budget");

        var fallback = tasks[0].Case ?? new FoamCaseData();
        Assert.That((fallback.CellCount, fallback.SolverClass), Is.EqualTo((900L, "incompressible-steady")), "a case without a size falls back to the block's");
        Assert.That(fallback.BaseFiles.Select(file => file.BlobId), Is.EqualTo(coarse.BaseFiles.Select(file => file.BlobId)));

        var uploadsAfter = Directory.Exists(m_storage) ? Directory.GetFiles(m_storage).Length : 0;
        Assert.That(uploadsAfter, Is.EqualTo(uploadsBefore), "a set chunk is metadata only: nothing is uploaded");
    }

    [Test]
    public void EveryMemberOfTheNodesCaseIsPlacedTest()
    {
        // The task's case is composed member by member from the set's case
        // and the study's block: a member FoamCaseData gains must be placed
        // in SweepFamilyOpenFOAMSet.CaseOf (from one or the other) before this
        // count moves.
        var members = typeof(FoamCaseData).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(property => property.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "MemoryPackOrderAttribute"));

        Assert.That(members, Is.EqualTo(8));
    }

    #endregion

    #region Row Tests

    [Test]
    public void ARowOfASetIsAnOpenFOAMRowTest()
    {
        var result = new FoamResultData { VariantIndex = 1, ArtifactBlobId = Guid.NewGuid(), ArtifactBytes = 2048 };
        var family = Family(Case(1, "fine"));

        var row = family.ToRows(new object[] { result }).Single();

        Assert.That(row.OpenFOAM, Is.SameAs(result));
        Assert.That(family.ArtifactsOf(row).Single().Kind, Is.EqualTo(SweepArtifactKind.OpenFOAMCase));
    }

    #endregion
}
