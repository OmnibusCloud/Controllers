namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// The declared source changes a layer needs to run under CPython-WASI. Each patch must
/// match exactly once, so a version bump that moves the code fails the build loudly
/// instead of shipping an unpatched layer. Every patch is named in the NOTICE.
/// </summary>
internal static class LayerPatches
{
    #region Patches

    /// <summary>
    /// sympy takes the C long size from ctypes at import; the WASI build has no _ctypes.
    /// wasm32: a C long is 4 bytes.
    /// </summary>
    public static readonly LayerPatch[] SYMPY =
    [
        new("sympy/external/gmpy.py",
            "from ctypes import c_long, sizeof\n",
            "",
            "the C long size comes from a constant, not from ctypes (no _ctypes in the WASI build)"),
        new("sympy/external/gmpy.py",
            "LONG_MAX = (1 << (8*sizeof(c_long) - 1)) - 1\n",
            "LONG_MAX = (1 << 31) - 1  # wasm32: a C long is 4 bytes; the WASI build has no _ctypes\n",
            "LONG_MAX = 2**31 - 1 (wasm32)")
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Applies the patches under a layer's source root.
    /// </summary>
    /// <param name="sourceRoot">The extracted layer.</param>
    /// <param name="patches">The patches to apply, in order.</param>
    /// <exception cref="InvalidOperationException">A patch does not match exactly once.</exception>
    public static void Apply(string sourceRoot, IReadOnlyList<LayerPatch> patches)
    {
        foreach (var patch in patches)
        {
            var path = Path.Combine(sourceRoot, patch.Path.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);
            var count = CountOccurrences(text, patch.Old);
            if (count != 1)
                throw new InvalidOperationException($"patch '{patch.Note}' matches {count} times in {patch.Path}, expected exactly once");

            File.WriteAllText(path, text.Replace(patch.Old, patch.New, StringComparison.Ordinal));
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    #endregion
}

/// <summary>One declared source change: the file, the exact text it replaces, its replacement, and why.</summary>
internal sealed record LayerPatch(string Path, string Old, string New, string Note);
