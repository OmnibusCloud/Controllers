using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// Native resolution where it broke (2.4.5-test.15): the cloud client's one shared load context,
/// where the binding assembly loads once — from the folder of the controller that needed it
/// first — and has one DllImport resolver, the first controller's. A controller shipped without
/// its natives (Corpus 0.2.0) then took the library away from every other: the resolver looked
/// next to the binding and found nothing. Here a second carrier of the host
/// (OutWit.Controller.AI.Verify.Tests.Carrier: the host's source and the binding, nothing else) is
/// loaded first by the client-like host and makes its host first, so its copy owns the binding and
/// the resolver; Verify follows in the same context. Each copy also answers the binding's load
/// context's <c>ResolvingUnmanagedDll</c> for its own folder, and only with the pinned library, so
/// the other controller's sandbox works whatever the first one carries. A library the runtime
/// finds by its own probing before any handler is asked keeps both sandboxes off. A process each.
/// </summary>
[TestFixture]
public sealed class WasmHostNativeResolutionTests
{
    #region Constants

    private const string NATIVE_LOADED = "wasmtime-native: loaded";
    private const string NATIVE_FAILED = "wasmtime-native: FAILED InvalidOperationException: ";
    private const string WASI_PASS = "wasi: Pass exit=0 stdout=hello";
    private const string MANAGED_PREFIX = "wasmtime-managed: ";
    private const string RESOLVER_PREFIX = "wasmtime-native-resolver: ";
    private const string CARRIER_PREFIX = "first: carrier: ";

    #endregion

    #region Fields

    private string m_root = null!;
    private string m_pinned = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_native_resolution_{Guid.NewGuid():N}");

        // The library of the test bin is the pinned one (the test build puts it there; the build
        // server does too): the copies below are the shipped bytes or a changed copy of them.
        m_pinned = WasmHostNativeLibrary.Locate(TestContext.CurrentContext.TestDirectory)!;
        Assert.That(m_pinned, Is.Not.Null, "the test bin carries runtimes/<rid>/native");
        Assert.That(WasmHostNatives.IsPinned(m_pinned), Is.True, "the test bin carries the pinned library");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch (IOException)
        {
            // the host process may still hold a file for a moment; the temp folder goes later
        }
        catch (UnauthorizedAccessException)
        {
            // as above
        }
    }

    #endregion

    #region Tests

    [Test]
    public void AControllerWithoutNativesLoadedFirstLeavesTheOtherItsSandboxTest()
    {
        var carrier = WasmHostClientHost.CopyCarrier(Path.Combine(m_root, "carrier"));
        var controllers = Path.Combine(m_root, "controllers");
        var verify = WasmHostClientHost.CopyVerifyModule(controllers);
        var natives = WasmHostClientHost.AddNatives(m_pinned, verify);

        var (exitCode, report) = WasmHostClientHost.Run(controllers, carrier);
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), report);
            Assert.That(report, Does.Contain($"{CARRIER_PREFIX}FAILED InvalidOperationException: no wasmtime library was found"), "the first carrier has no library and says so");
            Assert.That(WasmHostClientHost.Value(report, MANAGED_PREFIX), Does.StartWith(Path.GetDirectoryName(carrier)!).IgnoreCase,
                "the binding is the first carrier's, as in the client");
            Assert.That(report, Does.Contain(NATIVE_LOADED), report);
            Assert.That(report, Does.Contain(WASI_PASS), report);
            Assert.That(WasmHostClientHost.Value(report, RESOLVER_PREFIX), Is.EqualTo(natives).IgnoreCase, "Verify's handler loaded Verify's own library");
        });
    }

    [Test]
    public void AControllerWithAnotherLibraryLoadedFirstLeavesTheOtherItsSandboxTest()
    {
        var carrier = WasmHostClientHost.CopyCarrier(Path.Combine(m_root, "carrier"));
        var changed = Changed(WasmHostClientHost.AddNatives(m_pinned, Path.GetDirectoryName(carrier)!));
        var controllers = Path.Combine(m_root, "controllers");
        var verify = WasmHostClientHost.CopyVerifyModule(controllers);
        var natives = WasmHostClientHost.AddNatives(m_pinned, verify);

        var (exitCode, report) = WasmHostClientHost.Run(controllers, carrier);
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), report);
            Assert.That(report, Does.Contain($"{CARRIER_PREFIX}FAILED InvalidOperationException: {Mismatch(changed, "in this controller's folder")}"),
                "the first carrier refuses its library, naming both hashes");
            Assert.That(report, Does.Contain(NATIVE_LOADED), report);
            Assert.That(report, Does.Contain(WASI_PASS), report);
            Assert.That(WasmHostClientHost.Value(report, RESOLVER_PREFIX), Is.EqualTo(natives).IgnoreCase, "Verify's handler loaded Verify's own library");
        });
    }

    [Test]
    public void AControllerWithoutNativesRunsOnTheLibraryAnotherLoadedTest()
    {
        var carrier = WasmHostClientHost.CopyCarrier(Path.Combine(m_root, "carrier"));
        var carried = WasmHostClientHost.AddNatives(m_pinned, Path.GetDirectoryName(carrier)!);
        var controllers = Path.Combine(m_root, "controllers");
        WasmHostClientHost.CopyVerifyModule(controllers);

        var (exitCode, report) = WasmHostClientHost.Run(controllers, carrier);
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), report);
            Assert.That(report, Does.Contain($"{CARRIER_PREFIX}Exited 0 hello"), "the first carrier runs on its own library");
            Assert.That(report, Does.Contain(NATIVE_LOADED), "the library the binding already uses is the pinned one");
            Assert.That(report, Does.Contain(WASI_PASS), report);
            Assert.That(WasmHostClientHost.Value(report, RESOLVER_PREFIX), Does.StartWith(carried).IgnoreCase, "the other controller's library, as noted by its copy of the host");
        });
    }

    [Test]
    public void ALibraryOtherThanThePinnedOneKeepsTheSandboxOffTest()
    {
        var controllers = Path.Combine(m_root, "controllers");
        var verify = WasmHostClientHost.CopyVerifyModule(controllers);
        var changed = Changed(WasmHostClientHost.AddNatives(m_pinned, verify));

        var (exitCode, report) = WasmHostClientHost.Run(controllers);
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), report);
            Assert.That(report, Does.Contain($"{NATIVE_FAILED}{Mismatch(changed, "in this controller's folder")}"), report);
            Assert.That(report, Does.Not.Contain(NATIVE_LOADED), report);
        });
    }

    [Test]
    public void AnotherLibraryTheRuntimeFoundItselfKeepsTheSandboxOffTest()
    {
        // The first carrier ships the package's own library (48.0.2) beside its binding, where no
        // copy of the host looks but the runtime's probing does: it finds it before any handler is
        // asked, and the binding runs on it. Neither carrier may run then; both name what was loaded.
        var carrier = WasmHostClientHost.CopyCarrier(Path.Combine(m_root, "carrier"));
        var package = WasmHostClientHost.PackageLibrary();
        File.Copy(package, Path.Combine(Path.GetDirectoryName(carrier)!, WasmHostNativeLibrary.FileName));
        var found = WasmHostNatives.Sha256Of(package)!;
        Assert.That(WasmHostNatives.IsPinned(package), Is.False, "the package's library is not the pinned patch");

        var controllers = Path.Combine(m_root, "controllers");
        var verify = WasmHostClientHost.CopyVerifyModule(controllers);
        WasmHostClientHost.AddNatives(m_pinned, verify);

        var (exitCode, report) = WasmHostClientHost.Run(controllers, carrier);
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), report);
            Assert.That(report, Does.Contain($"{CARRIER_PREFIX}FAILED InvalidOperationException: {Mismatch(found, "loaded in this process")}"), report);
            Assert.That(report, Does.Contain($"{NATIVE_FAILED}{Mismatch(found, "loaded in this process")}"), report);
            Assert.That(report, Does.Not.Contain(NATIVE_LOADED), report);
        });
    }

    #endregion

    #region Tools

    /// <summary>Changes a library in place (a byte appended: another file to the hash, the same code to the loader).</summary>
    private static string Changed(string library)
    {
        using (var stream = new FileStream(library, FileMode.Append))
            stream.WriteByte(0);

        Assert.That(WasmHostNatives.IsPinned(library), Is.False);
        return WasmHostNatives.Sha256Of(library)!;
    }

    private static string Mismatch(string found, string where)
    {
        var rid = WasmHostNativeLibrary.PortableRid;
        return $"the wasmtime library {where} has SHA-256 {found}, but this controller ships wasmtime {WasmHostNatives.VERSION} for {rid}, " +
               $"SHA-256 {WasmHostNatives.PINNED[rid]}";
    }

    #endregion
}
