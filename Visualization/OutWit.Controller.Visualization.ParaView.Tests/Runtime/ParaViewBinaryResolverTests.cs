using System.Runtime.InteropServices;
using OutWit.Controller.Visualization.ParaView.Runtime;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// Binary resolution per RID: the environment override, the bundled runtime folder, FindExecutable.
/// </summary>
[TestFixture]
public sealed class ParaViewBinaryResolverTests
{
    #region Fields

    private string m_dir = null!;

    private string? m_ambientOverride;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        // The operator override must not leak into these tests from the ambient environment
        // (a developer pointing OUTWIT_PVPYTHON at a runtime for the real-runtime fixtures).
        m_ambientOverride = Environment.GetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH);
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, null);

        m_dir = Path.Combine(Path.GetTempPath(), $"pv_resolve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_dir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, m_ambientOverride);
        if (Directory.Exists(m_dir))
            Directory.Delete(m_dir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void EnvironmentOverrideWinsEvenWhenMissingTest()
    {
        var configured = Path.Combine(m_dir, "nowhere", "pvpython.exe");
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, configured);

        Assert.That(ParaViewBinaryResolver.Resolve(Path.Combine(m_dir, "module", "x.dll")), Is.EqualTo(configured));
    }

    [Test]
    public void BundledRuntimeIsFoundUnderTheModuleForThisPlatformTest()
    {
        var folder = ParaViewBinaryResolver.ResolveCurrentRuntimeFolder();
        if (folder == null)
            Assert.Ignore("unsupported platform");

        var module = Path.Combine(m_dir, "paraview.module");
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pvpython.exe" : "pvpython";
        var binDir = Path.Combine(module, ParaViewBinaryResolver.TOOL_DIRECTORY, folder, "ParaView-6.1.1", "bin");
        Directory.CreateDirectory(binDir);
        var exe = Path.Combine(binDir, exeName);
        File.WriteAllText(exe, "#!/bin/sh\n");

        var resolved = ParaViewBinaryResolver.Resolve(Path.Combine(module, "OutWit.Controller.Visualization.ParaView.dll"));

        Assert.That(resolved, Is.EqualTo(exe));
    }

    [Test]
    public void MissingRuntimeResolvesNullOrADevTimeFallbackTest()
    {
        var module = Path.Combine(m_dir, "paraview.module");
        Directory.CreateDirectory(module);

        var resolved = ParaViewBinaryResolver.Resolve(Path.Combine(module, "x.dll"));

        // On a developer machine the resolver may fall back to the staged module or the author-side
        // prerequisites tree; anything else would be a runtime found outside the documented places.
        if (resolved != null)
            Assert.That(resolved, Does.Contain("@Prerequisites").Or.Contain("@Controllers"), resolved);
        else
            Assert.That(resolved, Is.Null);
    }

    [Test]
    public void FindExecutablePrefersBinTest()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pvpython.exe" : "pvpython";
        Directory.CreateDirectory(Path.Combine(m_dir, "bin"));
        Directory.CreateDirectory(Path.Combine(m_dir, "deep", "er"));
        File.WriteAllText(Path.Combine(m_dir, "bin", exeName), "");
        File.WriteAllText(Path.Combine(m_dir, "deep", "er", exeName), "");

        Assert.That(ParaViewBinaryResolver.FindExecutable(m_dir), Is.EqualTo(Path.Combine(m_dir, "bin", exeName)));
    }

    #endregion
}
