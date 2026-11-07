using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Families;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;

namespace OutWit.Controller.Sweep.Tests.Families;

[TestFixture]
public class SweepFamilyCalculiXTests
{
    private string m_storage = null!;
    private SweepTestBlobService m_blobs = null!;
    private readonly SweepFamilyCalculiX m_family = new();

    [SetUp]
    public void Setup()
    {
        m_storage = Path.Combine(Path.GetTempPath(), $"sweep-ccx-family-{Guid.NewGuid():N}");
        m_blobs = new SweepTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storage))
            Directory.Delete(m_storage, recursive: true);
    }

    #region Tools

    private static SweepOptionsData DeckSet(params int[] deckIndices)
    {
        return new SweepOptionsData
        {
            Variants = [new SweepVariantData { VariantIndex = 0 }, new SweepVariantData { VariantIndex = 1 }],
            CalculiX = new SweepCalculiXStudyData
            {
                Decks = deckIndices.Select(index => new SweepCalculiXDeckData { VariantIndex = index, DeckBlobId = Guid.NewGuid() }).ToList()
            }
        };
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task ATemplateWhoseTokensAreInTheDeckIsAcceptedTest()
    {
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E", Token = "{{oc1}}" }],
            Variants = [new SweepVariantData { VariantIndex = 0, Values = ["210000"] }],
            CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = m_blobs.AddText("*MATERIAL\n{{oc1}}\n", "base.inp") }
        };

        Assert.That(await m_family.ValidateAsync(options, m_blobs), Is.Empty);
    }

    [Test]
    public async Task ATemplateWithoutItsTokenOrWithoutADeckIsRefusedTest()
    {
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E", Token = "{{oc1}}" }],
            Variants = [new SweepVariantData { VariantIndex = 0, Values = ["210000"] }],
            CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = m_blobs.AddText("*MATERIAL\n210000\n", "base.inp") }
        };

        Assert.That(await m_family.ValidateAsync(options, m_blobs), Has.Exactly(1).Items.And.Some.Contains("{{oc1}}"));

        options.CalculiX = new SweepCalculiXStudyData();
        Assert.That(await m_family.ValidateAsync(options, m_blobs), Is.EqualTo(new[] { "A CalculiX template study names no base deck." }));
    }

    [Test]
    public async Task ADeckSetMustCoverEveryVariantExactlyOnceTest()
    {
        Assert.That(await m_family.ValidateAsync(DeckSet(0, 1), m_blobs), Is.Empty);
        Assert.That(await m_family.ValidateAsync(DeckSet(0), m_blobs), Is.EqualTo(new[] { "Variant #1 has no deck in the deck set." }));
        Assert.That(await m_family.ValidateAsync(DeckSet(0, 1, 1), m_blobs), Is.EqualTo(new[] { "Variant #1 has 2 decks in the deck set." }));
        Assert.That(await m_family.ValidateAsync(DeckSet(0, 1, 5), m_blobs), Is.EqualTo(new[] { "The deck set carries a deck for variant #5, which the variant table does not have." }));
    }

    [Test]
    public async Task ADeckSetIsNeverAlsoATemplateTest()
    {
        var options = DeckSet(0, 1);
        options.Parameters = [new SweepParameterData { Name = "E", Token = "{{oc1}}" }];
        options.CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = Guid.NewGuid(), Decks = options.CalculiX?.Decks ?? [] };

        var findings = await m_family.ValidateAsync(options, m_blobs);

        Assert.That(findings, Has.Count.EqualTo(2));
        Assert.That(findings, Has.Some.Contains("either a deck set or a template"));
        Assert.That(findings, Has.Some.Contains("cannot also declare template parameters"));
    }

    #endregion

    #region Task Tests

    [Test]
    public async Task TemplateTasksCarryTheirOwnDeckAndTheStudysPoliciesTest()
    {
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E", Token = "{{oc1}}" }],
            Variants = [new SweepVariantData { VariantIndex = 4, Values = ["70000"] }],
            CalculiX = new SweepCalculiXStudyData
            {
                BaseDeckBlobId = m_blobs.AddText("E={{oc1}}\n", "base.inp"),
                NodeCount = 10,
                ElementCount = 8,
                Threads = 2,
                Extraction = new CcxExtractionRequestData()
            }
        };

        var tasks = await m_family.MakeTasksAsync(options, options.Variants, m_blobs);

        var task = tasks.Single() as CcxTaskData ?? new CcxTaskData();
        Assert.That(task.VariantIndex, Is.EqualTo(4));
        Assert.That(File.ReadAllText(m_blobs.GetStoredPath(task.DeckBlobId)), Is.EqualTo("E=70000\n"));
        Assert.That((task.NodeCount, task.ElementCount, task.Threads), Is.EqualTo((10, 8, 2)));
        Assert.That(task.Extraction, Is.Not.Null);
    }

    [Test]
    public async Task DeckSetTasksUseTheUploadedDeckAndItsOwnMeshSizeTest()
    {
        var deck = Guid.NewGuid();
        var options = new SweepOptionsData
        {
            Variants = [new SweepVariantData { VariantIndex = 0 }],
            CalculiX = new SweepCalculiXStudyData
            {
                Decks = [new SweepCalculiXDeckData { VariantIndex = 0, DeckBlobId = deck, NodeCount = 500 }],
                NodeCount = 1,
                ElementCount = 3
            }
        };

        var task = (await m_family.MakeTasksAsync(options, options.Variants, m_blobs)).Single() as CcxTaskData ?? new CcxTaskData();

        Assert.That(task.DeckBlobId, Is.EqualTo(deck));
        Assert.That(task.NodeCount, Is.EqualTo(500), "the deck's own count");
        Assert.That(task.ElementCount, Is.EqualTo(3), "the study's count where the deck gave none");
    }

    #endregion

    #region Row Tests

    [Test]
    public void ARowIsTheResultVerbatimWithTheVerdictTest()
    {
        var solved = new CcxResultData { VariantIndex = 1, ExitCode = 0, FrdBlobId = Guid.NewGuid(), DatBlobId = Guid.NewGuid() };
        var failed = new CcxResultData { VariantIndex = 2, ExitCode = 201, LogTail = "fake failure" };

        var rows = m_family.ToRows(new object[] { failed, "a foreign entry", solved });

        Assert.That(rows.Select(row => (row.VariantIndex, row.Outcome)), Is.EqualTo(new[] { (2, SweepOutcome.Failed), (1, SweepOutcome.Succeeded) }), "foreign entries are left out");
        Assert.That(rows[1].CalculiX, Is.SameAs(solved));
        Assert.That(rows[1].OpenFOAM, Is.Null);

        var artifacts = m_family.ArtifactsOf(rows[1]);
        Assert.That(artifacts.Select(artifact => artifact.Kind), Is.EqualTo(new[] { SweepArtifactKind.CalculiXFrd, SweepArtifactKind.CalculiXDat }));
        Assert.That(artifacts[0].BlobId, Is.EqualTo(solved.FrdBlobId));
        Assert.That(m_family.ArtifactsOf(rows[0]), Is.Empty);
    }

    #endregion
}
