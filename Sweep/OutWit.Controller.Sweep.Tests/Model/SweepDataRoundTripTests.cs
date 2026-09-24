using MemoryPack;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Tests.Model;

[TestFixture]
public class SweepDataRoundTripTests
{
    #region Tools

    private static SweepOptionsData CalculiXStudy()
    {
        return new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E", Token = "{{oc1}}" }],
            Variants = [new SweepVariantData { VariantIndex = 0, Values = ["210000"] }, new SweepVariantData { VariantIndex = 1, Values = ["70000"] }],
            FirstChunkSize = 2,
            MaxChunkSize = 8,
            CalculiX = new SweepCalculiXStudyData
            {
                BaseDeckBlobId = Guid.NewGuid(),
                NodeCount = 1200,
                ElementCount = 900,
                Threads = 4,
                Extraction = new CcxExtractionRequestData { Probes = [new CcxProbeData { Aggregate = CcxProbeAggregate.Max, Quantity = "von_mises", SetName = "NALL" }] }
            }
        };
    }

    private static SweepOptionsData OpenFOAMStudy()
    {
        return new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "U", Token = "{{oc1}}" }],
            Variants = [new SweepVariantData { VariantIndex = 0, Values = ["10"] }],
            OpenFOAM = new FoamCaseData
            {
                BaseFiles = [new FoamFileRefData { RelativePath = "0/U", BlobId = Guid.NewGuid(), Templated = true }],
                Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = [new FoamStepData { Utility = "simpleFoam" }] },
                CellCount = 12225
            }
        };
    }

    private static T RoundTrip<T>(T value)
    {
        var restored = MemoryPackSerializer.Deserialize<T>(MemoryPackSerializer.Serialize(value));
        Assert.That(restored, Is.Not.Null);
        return restored ?? value;
    }

    #endregion

    #region Options Tests

    [Test]
    public void BothFamiliesOptionsSurviveARoundTripTest()
    {
        var calculiX = CalculiXStudy();
        var openFoam = OpenFOAMStudy();

        Assert.That(RoundTrip(calculiX).Is(calculiX), Is.True);
        Assert.That(RoundTrip(openFoam).Is(openFoam), Is.True);
    }

    [Test]
    public void TheFamilyIsTheOneBlockThatIsSetTest()
    {
        Assert.That(CalculiXStudy().Family, Is.EqualTo(SweepFamily.CalculiX));
        Assert.That(OpenFOAMStudy().Family, Is.EqualTo(SweepFamily.OpenFOAM));
        Assert.That(new SweepOptionsData().Family, Is.Null, "no block");

        var both = CalculiXStudy();
        both.OpenFOAM = OpenFOAMStudy().OpenFOAM;
        Assert.That(both.Family, Is.Null, "two blocks");
        Assert.That(RoundTrip(both).Family, Is.Null, "the family is derived, never serialized");
    }

    [Test]
    public void AClonedStudyIsEqualAndIndependentTest()
    {
        var study = CalculiXStudy();
        var clone = study.Clone();

        Assert.That(clone.Is(study), Is.True);

        clone.Variants[1].Values[0] = "80000";
        Assert.That(clone.Is(study), Is.False);
        Assert.That(study.Variants[1].Values[0], Is.EqualTo("70000"));

        var other = study.Clone();
        other.CalculiX = new SweepCalculiXStudyData();
        Assert.That(other.Is(study), Is.False);
    }

    #endregion

    #region Manifest And State Tests

    [Test]
    public void ManifestRowsOfBothFamiliesSurviveARoundTripTest()
    {
        var manifest = new SweepManifestData
        {
            Rows =
            [
                new SweepManifestRowData
                {
                    VariantIndex = 0,
                    Outcome = SweepOutcome.Succeeded,
                    CalculiX = new CcxResultData { VariantIndex = 0, FrdBlobId = Guid.NewGuid(), SolveSeconds = 2.5, ResponseRow = new CcxResponseRowData { Values = [new CcxResponseValueData { Name = "max.von_mises", Value = 1.5e8 }] } }
                },
                new SweepManifestRowData
                {
                    VariantIndex = 1,
                    Outcome = SweepOutcome.Refused,
                    OpenFOAM = new FoamResultData { VariantIndex = 1, Rejections = ["0/p:1: codeStream compiles C++ at run time."] }
                }
            ]
        };

        var restored = RoundTrip(manifest);

        Assert.That(restored.Is(manifest), Is.True);
        Assert.That(restored.Rows[1].OpenFOAM?.Rejections, Has.Count.EqualTo(1));

        var changed = manifest.Clone();
        changed.Rows[0].Outcome = SweepOutcome.Failed;
        Assert.That(changed.Is(manifest), Is.False);
    }

    [Test]
    public void TheStateWithItsIndexSurvivesARoundTripTest()
    {
        var state = new SweepStateData
        {
            ChunkIndex = 2,
            NextVariantOrdinal = 7,
            SucceededCount = 5,
            FailedCount = 1,
            RefusedCount = 1,
            ManifestBlobId = Guid.NewGuid(),
            Results =
            [
                new SweepResultIndexEntryData
                {
                    VariantIndex = 0,
                    Outcome = SweepOutcome.Succeeded,
                    Label = "U=10",
                    Artifacts = [new SweepArtifactData { Kind = SweepArtifactKind.OpenFOAMCase, BlobId = Guid.NewGuid(), Bytes = 2048 }]
                }
            ]
        };

        var restored = RoundTrip(state);

        Assert.That(restored.Is(state), Is.True);
        Assert.That(restored.Results[0].Artifacts[0].Kind, Is.EqualTo(SweepArtifactKind.OpenFOAMCase));
        Assert.That(state.ToString(), Is.EqualTo("chunk 2: 5 succeeded, 1 failed, 1 refused"));
    }

    [Test]
    public void APlanSurvivesARoundTripTest()
    {
        var plan = new SweepPlanData { Options = OpenFOAMStudy(), ChunkSizes = [3, 3, 1] };

        Assert.That(RoundTrip(plan).Is(plan), Is.True);
    }

    #endregion
}
