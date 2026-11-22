using System.Reflection;
using System.Runtime.InteropServices;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The size of a compiled module: the bytes of machine code and data the engine holds for it
/// while it lives. The .NET binding does not expose it, so it is asked of the wasmtime C API on
/// the binding's own module (<c>wasmtime_module_image_range</c>), as the engine's settings are
/// (<see cref="WasmHostEngineNative"/>). A cache of compiled modules is bounded by this, not by a
/// count: a module of a few kilobytes can compile to hundreds of megabytes. Where the binding or
/// the library cannot say, the size is taken from the module's own bytes, on the high side.
/// </summary>
internal static class WasmHostModuleImage
{
    #region Constants

    private const string HANDLE_PROPERTY = "NativeHandle";
    private const string IMAGE_RANGE = "wasmtime_module_image_range";

    /// <summary>
    /// What a byte of a module is taken to compile to when the engine cannot be asked: the code
    /// of the modules the controllers ship takes 3 to 5 times their bytes.
    /// </summary>
    public const int ESTIMATE_PER_BYTE = 8;

    #endregion

    #region Delegates

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ImageRange(IntPtr module, out IntPtr start, out IntPtr end);

    #endregion

    #region Fields

    private static readonly Lazy<ImageRange?> RANGE = new(Find);

    #endregion

    #region Functions

    /// <summary>
    /// The bytes the engine holds for a compiled module.
    /// </summary>
    /// <param name="module">The compiled module.</param>
    /// <param name="moduleBytes">The size of the module it was compiled from, bytes.</param>
    /// <returns>The size of the compiled image; an estimate from the module's size when the engine cannot be asked.</returns>
    public static long Bytes(WasmModule module, long moduleBytes)
    {
        return Measure(module) ?? Estimate(moduleBytes);
    }

    /// <summary>
    /// The size of a compiled module as the engine reports it.
    /// </summary>
    /// <param name="module">The compiled module.</param>
    /// <returns>The bytes of the compiled image, or null when the binding or the library cannot say.</returns>
    public static long? Measure(WasmModule module)
    {
        var range = RANGE.Value;
        if (range == null)
            return null;

        var property = typeof(WasmModule).GetProperty(HANDLE_PROPERTY, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (property?.GetValue(module) is not SafeHandle handle || handle.IsInvalid)
            return null;

        var held = false;
        try
        {
            handle.DangerousAddRef(ref held);
            range(handle.DangerousGetHandle(), out var start, out var end);
            var size = end.ToInt64() - start.ToInt64();
            return size >= 0 ? size : null;
        }
        finally
        {
            if (held)
                handle.DangerousRelease();
        }
    }

    /// <summary>The size a module of some bytes is taken to compile to when the engine cannot be asked.</summary>
    /// <param name="moduleBytes">The size of the module, bytes.</param>
    /// <returns>Bytes.</returns>
    public static long Estimate(long moduleBytes)
    {
        return Math.Max(0, moduleBytes) * ESTIMATE_PER_BYTE;
    }

    private static ImageRange? Find()
    {
        try
        {
            return NativeLibrary.TryGetExport(WasmHostEngineNative.TheBindingsLibrary(), IMAGE_RANGE, out var address)
                ? Marshal.GetDelegateForFunctionPointer<ImageRange>(address)
                : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    #endregion
}
