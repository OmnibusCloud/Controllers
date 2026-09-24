using System.Security.Cryptography;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamKitIntegrityTests
{
    private string m_kit = null!;

    [SetUp]
    public void Setup()
    {
        m_kit = OpenFOAMTestPaths.CreateScratch("foam-integrity");
        Directory.CreateDirectory(Path.Combine(m_kit, "OpenFOAM-v2606", "etc"));
        Directory.CreateDirectory(Path.Combine(m_kit, "OpenFOAM-v2606", "lib"));
        File.WriteAllText(Path.Combine(m_kit, "KIT.env"), "PATH=@KIT@/bin\nWM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n");
        File.WriteAllText(Path.Combine(m_kit, "OpenFOAM-v2606", "etc", "controlDict"), "Documentation { }\n");
        for (var index = 0; index < 100; index++)
            File.WriteAllText(Path.Combine(m_kit, "OpenFOAM-v2606", "lib", $"lib{index:D3}.so"), $"library {index}\n");

        WriteBuildInfo();
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_kit);
    }

    #region Tools

    private void WriteBuildInfo()
    {
        var lines = new List<string> { "OpenFOAM v2606 (api 2606) - OmnibusCloud kit", "platform: test", "", "sha256 of every file (relative to the kit folder):" };
        foreach (var file in Directory.EnumerateFiles(m_kit, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == FoamKitIntegrity.BUILDINFO)
                continue;

            var relative = Path.GetRelativePath(m_kit, file).Replace('\\', '/');
            using var stream = File.OpenRead(file);
            lines.Add($"{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}  {relative}");
        }

        File.WriteAllLines(Path.Combine(m_kit, FoamKitIntegrity.BUILDINFO), lines);
    }

    #endregion

    #region Integrity Tests

    [Test]
    public void AnIntactKitHasNoFindingsAndTheSampleIsSpreadTest()
    {
        var findings = FoamKitIntegrity.Check(m_kit);

        Assert.That(findings, Is.Empty);

        var listed = FoamKitIntegrity.Parse(File.ReadAllLines(Path.Combine(m_kit, FoamKitIntegrity.BUILDINFO)));
        var sample = FoamKitIntegrity.Sample(listed, FoamKitIntegrity.SAMPLE_SIZE);
        Assert.That(listed, Has.Count.EqualTo(102));
        Assert.That(sample.Select(entry => entry.RelativePath), Does.Contain("KIT.env").And.Contain("OpenFOAM-v2606/etc/controlDict"));
        Assert.That(sample, Has.Count.InRange(FoamKitIntegrity.SAMPLE_SIZE, FoamKitIntegrity.SAMPLE_SIZE + 2));
        Assert.That(sample.Select(entry => entry.RelativePath).Distinct().Count(), Is.EqualTo(sample.Count));
        // Spread: the first and the last third of the library list are both represented.
        var sampledLibraries = sample
            .Select(entry => Path.GetFileNameWithoutExtension(entry.RelativePath))
            .Where(name => name.StartsWith("lib", StringComparison.Ordinal))
            .Select(name => int.Parse(name[3..]))
            .ToList();
        Assert.That(sampledLibraries, Has.Some.LessThan(33));
        Assert.That(sampledLibraries, Has.Some.GreaterThan(66));
        Assert.That(sampledLibraries, Has.Count.GreaterThanOrEqualTo(28));
    }

    [Test]
    public void AnAlteredAlwaysCheckedFileIsNamedTest()
    {
        File.AppendAllText(Path.Combine(m_kit, "KIT.env"), "TAMPERED=1\n");

        var findings = FoamKitIntegrity.Check(m_kit);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("KIT.env:").And.Contain("differs"));
    }

    [Test]
    public void AMissingSampledFileIsNamedTest()
    {
        // Every file removed: whatever the sample picks is missing.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(m_kit, "OpenFOAM-v2606", "lib")))
            File.Delete(file);

        var findings = FoamKitIntegrity.Check(m_kit);

        Assert.That(findings, Has.Count.GreaterThanOrEqualTo(FoamKitIntegrity.SAMPLE_SIZE - 2));
        Assert.That(findings, Has.All.Contains("missing from the kit"));
    }

    [Test]
    public void AKitWithoutABuildInfoIsOneFindingTest()
    {
        File.Delete(Path.Combine(m_kit, FoamKitIntegrity.BUILDINFO));

        var findings = FoamKitIntegrity.Check(m_kit);

        Assert.That(findings, Is.EqualTo(new[] { $"The kit carries no {FoamKitIntegrity.BUILDINFO}." }));
    }

    [Test]
    public void ParseReadsOnlyHashLinesTest()
    {
        var entries = FoamKitIntegrity.Parse(
        [
            "OpenFOAM v2606 (api 2606) - OmnibusCloud kit",
            "built at:        2026-09-23T13:54:08Z",
            "3fa9f9d0a9056f67c2e0cc27f3efc514e64b4ee20bf4093342632418b04b4d25  KIT.env",
            "not a hash line",
            "00edaa469fb8e5023aa495a0c8a73accd235d4a1a603ca1338c88112d09798eb  OpenFOAM-v2606/etc/controlDict"
        ]);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].RelativePath, Is.EqualTo("KIT.env"));
        Assert.That(entries[1].Sha256, Does.StartWith("00edaa46"));
    }

    #endregion
}
