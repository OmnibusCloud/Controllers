using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// verify-assets build &lt;out-dir&gt; [--cache DIR] [--work DIR] [--offline] [--clean]
///
/// Rebuilds the Verify runtime set from its pins into out-dir (the layout the controller's
/// data asset extracts to wasm/, and the layout the tests read from @Data/runtimes):
///
///   python-3.14.6/python.wasm                 CPython 3.14.6 for WASI, unchanged
///   python-3.14.6/lib/python314.zip           the standard library, bytecode, one file
///   python-3.14.6/lib/sympy-1.14.0.zip        sympy 1.14.0 + mpmath 1.4.1, bytecode, one file
///   python-3.14.6/LICENSE, LICENSE-sympy.txt, LICENSE-mpmath.txt
///   qjs-wasi.wasm                             QuickJS-NG 0.15.1 for WASI, built by the toolchain (toolchain/build.sh)
///   LICENSE-quickjs-ng.txt
///   ruby-3.4.1/ruby.wasm                      Ruby 3.4.1 for WASI (ruby.wasm 2.10.1), unchanged
///   ruby-3.4.1/root/usr/local/lib/ruby/...    its standard library and four bundled gems, as files
///   ruby-3.4.1/LICENSE-*.txt
///   lua-5.4.8/lua.wasm                        Lua 5.4.8 for WASI, built by the toolchain (toolchain/build.sh)
///   lua-5.4.8/LICENSE-lua.txt
///   typescript-sucrase-3.35.1/sucrase.js      sucrase 3.35.1's transform, bundled by the toolchain (toolchain/typescript)
///   typescript-sucrase-3.35.1/LICENSE-sucrase.txt
///   NOTICE.txt                                what, from where, patched how, hashed to what
///
/// The build is reproducible: run it twice and the hashes it prints are the ones the
/// runtime catalog pins.
/// </summary>
internal static class Program
{
    #region Constants

    private const string DEFAULT_CACHE = "@Downloads";
    private const string NOTICE = "NOTICE.txt";

    #endregion

    #region Entry

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "build")
            return Usage();

        var outDir = Path.GetFullPath(args[1]);
        var cacheDir = Path.GetFullPath(OptionValue(args, "--cache") ?? Path.Combine(Path.GetDirectoryName(outDir)!, DEFAULT_CACHE));
        var workDir = Path.GetFullPath(OptionValue(args, "--work") ?? Path.Combine(Path.GetTempPath(), "verify-assets-work"));
        var offline = args.Contains("--offline");
        var clean = args.Contains("--clean");

        try
        {
            await BuildAsync(outDir, cacheDir, workDir, offline, clean);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"verify-assets: {exception.Message}");
            return 1;
        }
    }

    #endregion

    #region Build

    private static async Task BuildAsync(string outDir, string cacheDir, string workDir, bool offline, bool clean)
    {
        Console.WriteLine($"out:   {outDir}");
        Console.WriteLine($"cache: {cacheDir}");
        Console.WriteLine($"work:  {workDir}");

        Console.WriteLine("downloads");
        var cpythonZip = await AssetDownloader.FetchAsync(AssetPins.CPYTHON, cacheDir, offline);
        var sympyWheel = await AssetDownloader.FetchAsync(AssetPins.SYMPY, cacheDir, offline);
        var mpmathWheel = await AssetDownloader.FetchAsync(AssetPins.MPMATH, cacheDir, offline);
        var rubyArchive = await AssetDownloader.FetchAsync(AssetPins.RUBY, cacheDir, offline);
        var rubyLicense = await AssetDownloader.FetchAsync(AssetPins.RUBY_LICENSE, cacheDir, offline);
        var rubyBsdl = await AssetDownloader.FetchAsync(AssetPins.RUBY_BSDL, cacheDir, offline);
        var rubyWasmLicense = await AssetDownloader.FetchAsync(AssetPins.RUBY_WASM_LICENSE, cacheDir, offline);

        Directory.CreateDirectory(workDir);
        var cpythonDir = Path.Combine(workDir, "cpython");
        if (Directory.Exists(cpythonDir))
            Directory.Delete(cpythonDir, recursive: true);
        ZipFile.ExtractToDirectory(cpythonZip, cpythonDir);

        if (clean && Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);

        var pythonDir = Path.Combine(outDir, AssetPins.PYTHON_RUNTIME_DIR);
        var libDir = Path.Combine(pythonDir, "lib");
        Directory.CreateDirectory(libDir);

        Console.WriteLine("python314.zip (the standard library)");
        var stdlib = PythonLayerBuilder.BuildStdlib(cpythonDir, workDir, Path.Combine(libDir, AssetPins.STDLIB_ZIP));
        Console.WriteLine($"  {stdlib.Entries} entries, {stdlib.Bytes:N0} bytes");

        Console.WriteLine($"{AssetPins.SYMPY_ZIP} (sympy {AssetPins.SYMPY_VERSION} + mpmath {AssetPins.MPMATH_VERSION})");
        var sympy = PythonLayerBuilder.BuildSympy(cpythonDir, workDir, sympyWheel, mpmathWheel, Path.Combine(libDir, AssetPins.SYMPY_ZIP));
        Console.WriteLine($"  {sympy.Entries} entries, {sympy.Bytes:N0} bytes");

        Console.WriteLine("modules and licences");
        File.Copy(Path.Combine(cpythonDir, "python.wasm"), Path.Combine(pythonDir, "python.wasm"), overwrite: true);
        File.Copy(Path.Combine(cpythonDir, "LICENSE"), Path.Combine(pythonDir, "LICENSE"), overwrite: true);
        PythonLayerBuilder.ExtractFile(sympyWheel, $"sympy-{AssetPins.SYMPY_VERSION}.dist-info/licenses/LICENSE", Path.Combine(pythonDir, "LICENSE-sympy.txt"));
        PythonLayerBuilder.ExtractFile(mpmathWheel, $"mpmath-{AssetPins.MPMATH_VERSION}.dist-info/licenses/LICENSE", Path.Combine(pythonDir, "LICENSE-mpmath.txt"));
        CopyBuiltModule(AssetPins.QUICKJS_SOURCE_DIR, AssetPins.QUICKJS_MODULE, AssetPins.QUICKJS_MODULE_SHA256, Path.Combine(outDir, AssetPins.QUICKJS_FILE));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "modules", AssetPins.QUICKJS_SOURCE_DIR, AssetPins.QUICKJS_LICENSE_FILE), Path.Combine(outDir, AssetPins.QUICKJS_LICENSE_FILE), overwrite: true);

        Console.WriteLine($"{AssetPins.RUBY_RUNTIME_DIR} (Ruby {AssetPins.RUBY_VERSION})");
        var rubyDir = Path.Combine(outDir, AssetPins.RUBY_RUNTIME_DIR);
        var ruby = RubyRuntimeBuilder.Build(rubyArchive, rubyDir);
        File.Copy(rubyLicense, Path.Combine(rubyDir, AssetPins.RUBY_LICENSE.FileName), overwrite: true);
        File.Copy(rubyBsdl, Path.Combine(rubyDir, AssetPins.RUBY_BSDL.FileName), overwrite: true);
        File.Copy(rubyWasmLicense, Path.Combine(rubyDir, AssetPins.RUBY_WASM_LICENSE.FileName), overwrite: true);
        var rubyTree = TreeHash.Compute(Path.Combine(rubyDir, RubyRuntimeBuilder.TREE_DIR));
        Console.WriteLine($"  {ruby.LibraryFiles} library files, {ruby.GemFiles} files of {AssetPins.RUBY_BUNDLED_GEMS.Length} bundled gems, {rubyTree.Bytes:N0} bytes in the tree");

        Console.WriteLine($"{AssetPins.LUA_RUNTIME_DIR} (Lua {AssetPins.LUA_VERSION}, built by the toolchain)");
        var luaDir = Path.Combine(outDir, AssetPins.LUA_RUNTIME_DIR);
        Directory.CreateDirectory(luaDir);
        CopyBuiltModule(AssetPins.LUA_RUNTIME_DIR, AssetPins.LUA_MODULE, AssetPins.LUA_MODULE_SHA256, Path.Combine(luaDir, AssetPins.LUA_MODULE));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "modules", AssetPins.LUA_RUNTIME_DIR, AssetPins.LUA_LICENSE), Path.Combine(luaDir, AssetPins.LUA_LICENSE), overwrite: true);

        Console.WriteLine($"{AssetPins.TYPESCRIPT_RUNTIME_DIR} (sucrase, bundled by the toolchain)");
        var typeScriptDir = Path.Combine(outDir, AssetPins.TYPESCRIPT_RUNTIME_DIR);
        Directory.CreateDirectory(typeScriptDir);
        CopyBuiltModule(AssetPins.TYPESCRIPT_RUNTIME_DIR, AssetPins.TYPESCRIPT_BUNDLE, AssetPins.TYPESCRIPT_BUNDLE_SHA256, Path.Combine(typeScriptDir, AssetPins.TYPESCRIPT_BUNDLE));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "modules", AssetPins.TYPESCRIPT_RUNTIME_DIR, AssetPins.TYPESCRIPT_LICENSE), Path.Combine(typeScriptDir, AssetPins.TYPESCRIPT_LICENSE), overwrite: true);

        var hashes = new (string Name, string Path)[]
        {
            ($"{AssetPins.PYTHON_RUNTIME_DIR}/python.wasm", Path.Combine(pythonDir, "python.wasm")),
            ($"{AssetPins.PYTHON_RUNTIME_DIR}/lib/{AssetPins.STDLIB_ZIP}", Path.Combine(libDir, AssetPins.STDLIB_ZIP)),
            ($"{AssetPins.PYTHON_RUNTIME_DIR}/lib/{AssetPins.SYMPY_ZIP}", Path.Combine(libDir, AssetPins.SYMPY_ZIP)),
            (AssetPins.QUICKJS_FILE, Path.Combine(outDir, AssetPins.QUICKJS_FILE)),
            ($"{AssetPins.RUBY_RUNTIME_DIR}/{RubyRuntimeBuilder.MODULE_FILE}", Path.Combine(rubyDir, RubyRuntimeBuilder.MODULE_FILE)),
            ($"{AssetPins.LUA_RUNTIME_DIR}/{AssetPins.LUA_MODULE}", Path.Combine(luaDir, AssetPins.LUA_MODULE)),
            ($"{AssetPins.TYPESCRIPT_RUNTIME_DIR}/{AssetPins.TYPESCRIPT_BUNDLE}", Path.Combine(typeScriptDir, AssetPins.TYPESCRIPT_BUNDLE))
        }.Select(file => (file.Name, Sha256: AssetDownloader.Sha256Hex(file.Path), Bytes: new FileInfo(file.Path).Length)).ToList();

        // A folder pinned as a whole: the tree hash, the files' bytes.
        hashes.Add(($"{AssetPins.RUBY_RUNTIME_DIR}/{RubyRuntimeBuilder.TREE_DIR}/ (tree of {rubyTree.Files} files)", rubyTree.Sha256, rubyTree.Bytes));

        File.WriteAllText(Path.Combine(outDir, NOTICE), Notice(hashes, ruby), new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine("the catalog pins (SHA-256):");
        foreach (var (name, sha256, bytes) in hashes)
            Console.WriteLine($"  {sha256}  {name}  ({bytes:N0} bytes)");
    }

    /// <summary>
    /// Copies a module the toolchain built (modules/, next to this tool) into the set after
    /// checking it against its pin, so a changed module cannot slip in.
    /// </summary>
    private static void CopyBuiltModule(string sourceDir, string module, string sha256, string destination)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "modules", sourceDir, module);
        var hash = AssetDownloader.Sha256Hex(source);
        if (!string.Equals(hash, sha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"{source} is {hash}, the pin is {sha256}");

        File.Copy(source, destination, overwrite: true);
    }

    private static string Notice(IReadOnlyList<(string Name, string Sha256, long Bytes)> hashes, RubyRuntimeSummary ruby)
    {
        var text = new StringBuilder();
        text.Append("Language runtime modules for the OmnibusCloud Verify controller (WebAssembly/WASI,\n");
        text.Append("platform-independent). Pinned by SHA-256 in the controller's runtime catalog and\n");
        text.Append("rebuilt, byte for byte, by OutWit.Controller.AI.Verify.Assets (verify-assets build).\n\n");
        text.Append($"{AssetPins.PYTHON_RUNTIME_DIR}/python.wasm\n");
        text.Append($"    CPython {AssetPins.PYTHON_VERSION} built for WASI (wasi-sdk 24), taken unchanged from\n");
        text.Append($"    {AssetPins.CPYTHON.Url}\n");
        text.Append($"    Licence: {AssetPins.PYTHON_RUNTIME_DIR}/LICENSE (PSF).\n");
        text.Append($"{AssetPins.PYTHON_RUNTIME_DIR}/lib/{AssetPins.STDLIB_ZIP}\n");
        text.Append("    The standard library of that build as bytecode (unchecked-hash .pyc compiled by\n");
        text.Append("    python.wasm itself; sources and site-packages left out), one stored zip on sys.path.\n");
        text.Append($"{AssetPins.PYTHON_RUNTIME_DIR}/lib/{AssetPins.SYMPY_ZIP}\n");
        text.Append($"    sympy {AssetPins.SYMPY_VERSION} and mpmath {AssetPins.MPMATH_VERSION} from their PyPI wheels\n");
        text.Append($"    ({AssetPins.SYMPY.FileName}, {AssetPins.MPMATH.FileName}), as bytecode the same way,\n");
        text.Append("    test suites left out. Licences: LICENSE-sympy.txt, LICENSE-mpmath.txt (BSD-3-Clause).\n");
        text.Append("    Patched before compiling:\n");
        foreach (var patch in LayerPatches.SYMPY)
            text.Append($"      {patch.Path}: {patch.Note}\n");
        text.Append($"{AssetPins.QUICKJS_FILE}\n");
        text.Append($"    QuickJS-NG {AssetPins.QUICKJS_VERSION} (https://github.com/quickjs-ng/quickjs, tag v{AssetPins.QUICKJS_VERSION}), built for\n");
        text.Append("    WASI by the controller's toolchain (wasi-sdk 34): the C stack (1 MiB) below the data, and one\n");
        text.Append("    patch, toolchain/quickjs-wasi.patch, that turns the engine's stack limit back on (128 KiB),\n");
        text.Append("    so a recursion too deep is a RangeError. The project's own WASI build placed a 64 KiB stack\n");
        text.Append("    after its data with the limit off.\n");
        text.Append($"    Licence: {AssetPins.QUICKJS_LICENSE_FILE} (MIT).\n");
        text.Append($"{AssetPins.RUBY_RUNTIME_DIR}/{RubyRuntimeBuilder.MODULE_FILE}\n");
        text.Append($"    Ruby {AssetPins.RUBY_VERSION} built for WASI by the ruby.wasm project (release {AssetPins.RUBY_WASM_RELEASE}, the\n");
        text.Append("    'full' profile), taken unchanged (usr/local/bin/ruby) from\n");
        text.Append($"    {AssetPins.RUBY.Url}\n");
        text.Append($"    Licences: {AssetPins.RUBY_RUNTIME_DIR}/{AssetPins.RUBY_LICENSE.FileName} and {AssetPins.RUBY_BSDL.FileName} (Ruby's own terms or\n");
        text.Append($"    BSD-2-Clause), {AssetPins.RUBY_WASM_LICENSE.FileName} (the port, MIT).\n");
        text.Append($"{AssetPins.RUBY_RUNTIME_DIR}/{RubyRuntimeBuilder.TREE_DIR}/\n");
        text.Append($"    From the same archive, unchanged: the standard library (usr/local/lib/ruby/{AssetPins.RUBY_API_VERSION},\n");
        text.Append($"    {ruby.LibraryFiles} files) and the library folders of the bundled gems\n");
        text.Append($"    {string.Join(", ", AssetPins.RUBY_BUNDLED_GEMS)} ({ruby.GemFiles} files). Headers, the static\n");
        text.Append("    library, man pages and the other gems are left out. Pinned as a tree: one SHA-256\n");
        text.Append("    over the relative paths and the contents of every file.\n");
        if (ruby.Licences.Count > 0)
            text.Append($"    Licences of the gems: {string.Join(", ", ruby.Licences)}.\n");
        text.Append($"{AssetPins.LUA_RUNTIME_DIR}/{AssetPins.LUA_MODULE}\n");
        text.Append($"    Lua {AssetPins.LUA_VERSION} (https://www.lua.org/ftp/lua-{AssetPins.LUA_VERSION}.tar.gz), the stock interpreter built\n");
        text.Append("    for WASI by the controller's toolchain (wasi-sdk 34, Binaryen 133): Lua's files unchanged, an\n");
        text.Append("    error unwinds to its protected call by asyncify instead of WebAssembly's exception handling.\n");
        text.Append($"    Licence: {AssetPins.LUA_RUNTIME_DIR}/{AssetPins.LUA_LICENSE} (MIT).\n");
        text.Append($"{AssetPins.TYPESCRIPT_RUNTIME_DIR}/{AssetPins.TYPESCRIPT_BUNDLE}\n");
        text.Append("    sucrase 3.35.1's transform with what it needs (lines-and-columns, ts-interface-checker,\n");
        text.Append("    @jridgewell/*), from npm by the integrity hashes of toolchain/typescript/package-lock.json,\n");
        text.Append("    bundled for QuickJS by esbuild 0.25.10 (toolchain/typescript/bundle.sh), unchanged otherwise.\n");
        text.Append($"    Licences: {AssetPins.TYPESCRIPT_RUNTIME_DIR}/{AssetPins.TYPESCRIPT_LICENSE} (MIT; ts-interface-checker Apache-2.0).\n");
        text.Append('\n');
        text.Append("SHA-256:\n");
        foreach (var (name, sha256, bytes) in hashes)
            text.Append($"  {sha256}  {name}  ({bytes} bytes)\n");
        return text.ToString();
    }

    #endregion

    #region Tools

    private static string? OptionValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Usage()
    {
        Console.WriteLine("verify-assets build <out-dir> [--cache DIR] [--work DIR] [--offline] [--clean]");
        Console.WriteLine();
        Console.WriteLine("  out-dir   the runtime set to write: @Prerequisites/wasm for a release,");
        Console.WriteLine("            OutWit.Controller.AI.Verify.Tests/@Data/runtimes for the tests");
        Console.WriteLine("  --cache   pinned downloads (default: <out-dir>/../@Downloads)");
        Console.WriteLine("  --work    scratch folder (default: the temp folder)");
        Console.WriteLine("  --offline never download; the cache must hold every pin");
        Console.WriteLine("  --clean   wipe out-dir first");
        return 2;
    }

    #endregion
}
