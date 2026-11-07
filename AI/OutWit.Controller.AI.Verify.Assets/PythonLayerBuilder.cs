using System.IO.Compression;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Builds the two Python layers as zips CPython's zipimport loads straight from sys.path:
/// the standard library (python314.zip, on sys.path by default) and the sympy + mpmath layer
/// (named on PYTHONPATH by the catalog). Each layer is bytecode only — compiled by the pinned
/// interpreter itself as unchecked-hash .pyc, so an import neither parses source nor stats
/// it — plus the packages' data files. No sources means no source lines in tracebacks for
/// library frames, which the sandbox never compares; it means an import that costs a tenth.
/// </summary>
internal static class PythonLayerBuilder
{
    #region Constants

    private const string PY = ".py";
    private const string PYCACHE = "__pycache__";
    private const string TESTS = "tests";

    #endregion

    #region Functions

    /// <summary>
    /// Builds python314.zip from the extracted CPython-WASI tree.
    /// </summary>
    /// <param name="cpythonDir">The pristine extraction (python.wasm, lib/python3.14).</param>
    /// <param name="workDir">A scratch folder.</param>
    /// <param name="zipPath">The layer to write.</param>
    /// <returns>The layer's entry count and bytes.</returns>
    public static LayerBuild BuildStdlib(string cpythonDir, string workDir, string zipPath)
    {
        var source = Fresh(Path.Combine(workDir, "stdlib"));
        var stdlib = Path.Combine(cpythonDir, "lib", AssetPins.PYTHON_LIB_DIR);

        // site-packages stays out: the layer is the standard library, nothing else.
        CopyTree(stdlib, source, relative => !relative.StartsWith("site-packages/", StringComparison.Ordinal));

        Compile(cpythonDir, source, "/lib/" + AssetPins.STDLIB_ZIP);

        var entries = Collect(source, relative => !relative.EndsWith(PY, StringComparison.Ordinal) && !relative.Contains(PYCACHE + "/", StringComparison.Ordinal));
        DeterministicZip.Write(zipPath, entries);
        return new LayerBuild(entries.Count, new FileInfo(zipPath).Length);
    }

    /// <summary>
    /// Builds the sympy + mpmath layer from the two wheels.
    /// </summary>
    /// <param name="cpythonDir">The pristine CPython extraction (the compiler).</param>
    /// <param name="workDir">A scratch folder.</param>
    /// <param name="sympyWheel">The verified sympy wheel.</param>
    /// <param name="mpmathWheel">The verified mpmath wheel.</param>
    /// <param name="zipPath">The layer to write.</param>
    /// <returns>The layer's entry count and bytes.</returns>
    public static LayerBuild BuildSympy(string cpythonDir, string workDir, string sympyWheel, string mpmathWheel, string zipPath)
    {
        var source = Fresh(Path.Combine(workDir, "sympy"));

        ExtractPackage(sympyWheel, "sympy", source);
        ExtractPackage(mpmathWheel, "mpmath", source);

        LayerPatches.Apply(source, LayerPatches.SYMPY);

        // The packages' own test suites are a third of the bytes and never imported by a task.
        foreach (var tests in Directory.GetDirectories(source, TESTS, SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            Directory.Delete(tests, recursive: true);

        Compile(cpythonDir, source, "/lib/" + AssetPins.SYMPY_ZIP);

        var entries = Collect(source, relative => !relative.EndsWith(PY, StringComparison.Ordinal));
        DeterministicZip.Write(zipPath, entries);
        return new LayerBuild(entries.Count, new FileInfo(zipPath).Length);
    }

    /// <summary>
    /// Copies one file out of a wheel (e.g. its licence).
    /// </summary>
    /// <param name="wheel">The wheel.</param>
    /// <param name="entryName">The entry inside it.</param>
    /// <param name="destination">Where to write it.</param>
    /// <exception cref="InvalidOperationException">The wheel has no such entry.</exception>
    public static void ExtractFile(string wheel, string entryName, string destination)
    {
        using var archive = ZipFile.OpenRead(wheel);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"{Path.GetFileName(wheel)} has no entry {entryName}");
        entry.ExtractToFile(destination, overwrite: true);
    }

    private static void Compile(string cpythonDir, string sourceDir, string guestZipPath)
    {
        // Legacy .pyc next to each .py (what zipimport looks for), unchecked-hash (no source
        // validation at import), file names rewritten to where the zip will put them.
        var result = WasiPython.Run(Path.Combine(cpythonDir, "python.wasm"), cpythonDir, sourceDir,
        [
            "-B", "-m", "compileall", "-b", "-q", "-f",
            "--invalidation-mode", "unchecked-hash",
            "-s", WasiPython.GUEST_SOURCE, "-p", guestZipPath,
            WasiPython.GUEST_SOURCE
        ]);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"compileall failed ({result.ExitCode}):\n{result.Stderr}");

        var warnings = result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var compiled = Directory.GetFiles(sourceDir, "*.pyc", SearchOption.AllDirectories).Length;
        Console.WriteLine($"  compiled {compiled} modules with the pinned interpreter ({warnings} compiler message line(s))");
    }

    private static void ExtractPackage(string wheel, string package, string destination)
    {
        using var archive = ZipFile.OpenRead(wheel);
        var prefix = package + "/";
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
                continue;

            var target = Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static List<ZipEntryBytes> Collect(string root, Func<string, bool> include)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(include)
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .Select(relative => new ZipEntryBytes(relative, File.ReadAllBytes(Path.Combine(root, relative))))
            .ToList();
    }

    private static void CopyTree(string from, string to, Func<string, bool> include)
    {
        foreach (var path in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, path).Replace('\\', '/');
            if (!include(relative))
                continue;

            var target = Path.Combine(to, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
        }
    }

    private static string Fresh(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
        return directory;
    }

    #endregion
}

/// <summary>What a layer build produced.</summary>
internal sealed record LayerBuild(int Entries, long Bytes);
