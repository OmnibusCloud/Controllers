using MemoryPack;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Tests.Model;

[TestFixture]
public class CcxDataRoundTripTests
{
    #region Tools

    private static CcxTaskData CreateTask()
    {
        return new CcxTaskData
        {
            VariantIndex = 7,
            DeckBlobId = Guid.NewGuid(),
            NodeCount = 184302,
            ElementCount = 121850,
            Threads = 4,
            Extraction = new CcxExtractionRequestData
            {
                Probes =
                [
                    new CcxProbeData { Aggregate = CcxProbeAggregate.Max, Quantity = "von_mises", SetName = "WELD_TOE" }
                ]
            }
        };
    }

    private static CcxResultData CreateResult()
    {
        return new CcxResultData
        {
            VariantIndex = 7,
            FrdBlobId = Guid.NewGuid(),
            DatBlobId = Guid.NewGuid(),
            ExitCode = 0,
            SolveSeconds = 351.2,
            ResponseRow = new CcxResponseRowData
            {
                Values = [new CcxResponseValueData { Name = "max_von_mises", Value = 318.4 }]
            }
        };
    }

    #endregion

    #region Round Trip Tests

    [Test]
    public void TaskSurvivesMemoryPackRoundTripTest()
    {
        var task = CreateTask();
        var restored = MemoryPackSerializer.Deserialize<CcxTaskData>(MemoryPackSerializer.Serialize(task));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(task), Is.True);
    }

    [Test]
    public void ResultSurvivesMemoryPackRoundTripTest()
    {
        var result = CreateResult();
        var restored = MemoryPackSerializer.Deserialize<CcxResultData>(MemoryPackSerializer.Serialize(result));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(result), Is.True);
    }

    [Test]
    public void VariantSurvivesMemoryPackRoundTripTest()
    {
        var variant = new SweepVariantData
        {
            VariantIndex = 4,
            Values = ["250"],
            DeckBlobId = Guid.NewGuid(),
            NodeCount = 132651,
            ElementCount = 125000
        };

        var restored = MemoryPackSerializer.Deserialize<SweepVariantData>(MemoryPackSerializer.Serialize(variant));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(variant), Is.True);

        // The deck-set fields participate in value identity.
        var other = variant.Clone();
        other.DeckBlobId = Guid.NewGuid();
        Assert.That(other.Is(variant), Is.False);
    }

    [Test]
    public void ManifestSurvivesMemoryPackRoundTripTest()
    {
        var manifest = new SweepManifestData
        {
            Rows =
            [
                new SweepManifestRowData
                {
                    VariantIndex = 3,
                    Succeeded = false,
                    ExitCode = 201,
                    SolveSeconds = 12.5,
                    LogTail = "*ERROR in tail"
                }
            ]
        };

        var restored = MemoryPackSerializer.Deserialize<SweepManifestData>(MemoryPackSerializer.Serialize(manifest));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(manifest), Is.True);
    }

    #endregion

    #region Clone Tests

    [Test]
    public void TaskCloneIsValueEqualAndIndependentTest()
    {
        var task = CreateTask();
        var clone = task.Clone();

        Assert.That(clone.Is(task), Is.True);

        clone.Extraction!.Probes[0].SetName = "OTHER_SET";
        Assert.That(clone.Is(task), Is.False);
        Assert.That(task.Extraction!.Probes[0].SetName, Is.EqualTo("WELD_TOE"));
    }

    [Test]
    public void ResultDifferingByExitCodeIsNotEqualTest()
    {
        var result = CreateResult();
        var other = result.Clone();
        other.ExitCode = 1;

        Assert.That(other.Is(result), Is.False);
    }

    #endregion
}
