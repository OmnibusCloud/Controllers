using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Runtimes;

/// <summary>
/// The hash pin: a runtime resolves only when its module's content matches the pinned
/// SHA-256. Reproducibility and byte-comparison integrity both rest on this, so a
/// tampered or truncated module must resolve to "unavailable", never run.
/// </summary>
[TestFixture]
public sealed class VerifyRuntimeCatalogTests
{
    [Test]
    public void KnownIdsAreEnumeratedTest()
    {
        Assert.That(VerifyRuntimeCatalog.KnownIds,
            Is.SupersetOf(new[] { VerifyRuntimeCatalog.PYTHON_3_14_6, VerifyRuntimeCatalog.QUICKJS_0_15_1 }));
    }

    [Test]
    public void UnknownRuntimeIsUnavailableWithReasonTest()
    {
        var resolved = VerifyRuntimeCatalog.Resolve(Path.GetTempPath(), "cobol-1959", out var reason);

        Assert.That(resolved, Is.Null);
        Assert.That(reason, Does.Contain("unknown runtime id"));
    }

    [Test]
    public void MissingModuleIsUnavailableWithReasonTest()
    {
        var emptyRoot = Path.Combine(Path.GetTempPath(), $"witai_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyRoot);
        try
        {
            var resolved = VerifyRuntimeCatalog.Resolve(emptyRoot, VerifyRuntimeCatalog.QUICKJS_0_15_1, out var reason);

            Assert.That(resolved, Is.Null);
            Assert.That(reason, Does.Contain("not found"));
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

    [Test]
    public void TamperedModuleFailsHashPinTest()
    {
        // A module whose bytes don't match the pin must be rejected — the core DE-5 guarantee.
        var root = Path.Combine(Path.GetTempPath(), $"witai_tamper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "qjs-wasi.wasm"), "not a real wasm module");

            var resolved = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.QUICKJS_0_15_1, out var reason);

            Assert.That(resolved, Is.Null);
            Assert.That(reason, Does.Contain("SHA-256"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

/// <summary>Pure unit tests for the memory-bounded parallelism degree.</summary>
[TestFixture]
public sealed class VerifySandboxDegreeTests
{
    [TestCase(16, 8L * 1024 * 1024 * 1024, 256L * 1024 * 1024, ExpectedResult = 16)] // cores bind
    [TestCase(32, 4L * 1024 * 1024 * 1024, 512L * 1024 * 1024, ExpectedResult = 8)]  // memory binds
    [TestCase(1, 64L * 1024 * 1024 * 1024, 256L * 1024 * 1024, ExpectedResult = 1)]  // single core
    [TestCase(8, 100L * 1024 * 1024, 256L * 1024 * 1024, ExpectedResult = 1)]        // less RAM than one task
    public int DegreeIsMinOfCoresAndMemory(int cores, long ram, long perTask)
    {
        return VerifySandboxDegree.Compute(cores, ram, perTask);
    }

    [Test]
    public void ZeroPerTaskCapFallsBackToCoresTest()
    {
        Assert.That(VerifySandboxDegree.Compute(12, 8L * 1024 * 1024 * 1024, 0), Is.EqualTo(12));
    }
}
