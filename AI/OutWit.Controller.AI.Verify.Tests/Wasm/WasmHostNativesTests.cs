using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The natives guard on its own: the pinned table and the words of its refusal, the library of this
/// process, and the key a compiled module is valid for (the natives' hash and the trap mode). The
/// guard where it matters, the client's shared load context, is
/// <see cref="WasmHostNativeResolutionTests"/>.
/// </summary>
[TestFixture]
public sealed class WasmHostNativesTests
{
    #region Constants

    private const string RID = "win-x64";
    private const string WHERE = "loaded in this process";
    private const string OTHER = "0000000000000000000000000000000000000000000000000000000000000000";

    #endregion

    #region Guard Tests

    [Test]
    public void ThePinnedLibraryPassesTest()
    {
        Assert.That(WasmHostNatives.Check(RID, WasmHostNatives.PINNED[RID], WHERE), Is.Null);
    }

    [Test]
    public void AnotherLibraryIsRefusedNamingBothHashesTest()
    {
        Assert.That(WasmHostNatives.Check(RID, OTHER, WHERE), Is.EqualTo(
            $"the wasmtime library loaded in this process has SHA-256 {OTHER}, but this controller ships wasmtime {WasmHostNatives.VERSION} for {RID}, " +
            $"SHA-256 {WasmHostNatives.PINNED[RID]}; the sandbox stays off (in a shared load context the first controller to resolve the library decides it)"));
    }

    [Test]
    public void NoLibraryIsRefusedNamingTheExpectedHashTest()
    {
        Assert.That(WasmHostNatives.Check(RID, null, "in this controller's folder"), Is.EqualTo(
            $"no wasmtime library was found in this controller's folder; this controller ships wasmtime {WasmHostNatives.VERSION} for {RID}, " +
            $"SHA-256 {WasmHostNatives.PINNED[RID]}; the sandbox stays off"));
    }

    [Test]
    public void APlatformWithoutAPinnedLibraryIsRefusedTest()
    {
        Assert.That(WasmHostNatives.Check("linux-arm64", WasmHostNatives.PINNED[RID], WHERE),
            Does.StartWith("this controller ships no wasmtime library for linux-arm64 (it ships one for win-x64, linux-x64, osx-arm64)"));
    }

    [Test]
    public void TheLibraryOfThisProcessIsThePinnedOneTest()
    {
        using var sandbox = new VerifyWasmSandbox(Path.Combine(Path.GetTempPath(), $"witai_natives_{Guid.NewGuid():N}"));

        Assert.Multiple(() =>
        {
            Assert.That(WasmHostNatives.LoadedProblem(), Is.Null);
            Assert.That(WasmHostNatives.Sha256Of(WasmHostNativeLibrary.LoadedPath!), Is.EqualTo(WasmHostNatives.PINNED[WasmHostNativeLibrary.PortableRid]));
            Assert.That(VerifyEngineVersion.Current, Does.EndWith($"(native {WasmHostNatives.VERSION})"));
        });
    }

    [Test]
    public void TheShippedTableIsThePinnedOneTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifyEngineVersion.SHIPPED_NATIVES.Keys, Is.EquivalentTo(WasmHostNatives.PINNED.Values));
            Assert.That(VerifyEngineVersion.SHIPPED_NATIVES.Values, Is.All.EqualTo(WasmHostNatives.VERSION));
        });
    }

    #endregion

    #region Compiled Code Tests

    [Test]
    public void CompiledCodeIsKeyedByTheNativesAndTheTrapModeTest()
    {
        var pinned = WasmHostNatives.PINNED[RID];
        var keys = new[]
        {
            WasmHost.CompiledCodeKeyOf(pinned, explicitTraps: true),
            WasmHost.CompiledCodeKeyOf(pinned, explicitTraps: false),
            WasmHost.CompiledCodeKeyOf(OTHER, explicitTraps: true),
            WasmHost.CompiledCodeKeyOf(OTHER, explicitTraps: false)
        };

        Assert.Multiple(() =>
        {
            Assert.That(keys, Is.Unique);
            Assert.That(keys[0], Does.Contain(pinned).And.Contain("explicit"));
            Assert.That(keys[1], Does.Contain(pinned).And.Contain("signal"));
        });
    }

    [Test]
    public void TheHostsKeyNamesItsLibraryAndItsTrapModeTest()
    {
        using var host = new WasmHost();

        Assert.That(host.CompiledCodeKey, Is.EqualTo(WasmHost.CompiledCodeKeyOf(WasmHostNatives.PINNED[WasmHostNativeLibrary.PortableRid], host.ExplicitTraps)));
    }

    [Test]
    public void TheTaskModuleCacheKeysByTheHostsKeyTest()
    {
        byte[] bytes = [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00];
        var pinned = WasmHostNatives.PINNED[RID];

        Assert.Multiple(() =>
        {
            Assert.That(VerifyTaskModuleCache.KeyOf(WasmHost.CompiledCodeKeyOf(pinned, true), bytes),
                Is.Not.EqualTo(VerifyTaskModuleCache.KeyOf(WasmHost.CompiledCodeKeyOf(pinned, false), bytes)), "another trap mode, another entry");
            Assert.That(VerifyTaskModuleCache.KeyOf(WasmHost.CompiledCodeKeyOf(pinned, true), bytes),
                Is.Not.EqualTo(VerifyTaskModuleCache.KeyOf(WasmHost.CompiledCodeKeyOf(OTHER, true), bytes)), "other natives, another entry");
        });
    }

    #endregion
}
