using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Tests.Carrier;

/// <summary>
/// What a controller carrying the shared wasm host does first: it makes its host (the binding
/// guard, its resolver and handler, the engine, the natives guard) and runs one WASI command module.
/// Called by reflection from the client-like host; returns one line and never throws.
/// </summary>
public static class CarrierProbe
{
    #region Constants

    /// <summary>The line's prefix.</summary>
    public const string PREFIX = "carrier: ";

    // The 224-byte WASI command module that writes "hello\n" and returns (Verify's self-check probe).
    private const string HELLO_WASM_BASE64 =
        "AGFzbQEAAAABEANgBH9/f38Bf2ABfwBgAAACZwMWd2FzaV9zbmFwc2hvdF9wcmV2aWV3MQhmZF93cml0ZQAAFndhc2lfc25hcHNob3RfcHJldmlldzEHZmRfcmVhZAAAFndhc2lfc25hcHNob3RfcHJldmlldzEJcHJvY19leGl0AAEDAgECBQMBAAEHEwIGbWVtb3J5AgAGX3N0YXJ0AAMKDwENAEEBQQBBAUEIEAAaCwssAQBBAAsmIAAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABoZWxsbwo=";

    #endregion

    #region Functions

    /// <summary>Makes a host and runs the hello module in it.</summary>
    /// <returns>"carrier: Exited 0 hello" with the library the binding uses, or "carrier: FAILED" with the reason.</returns>
    public static string Run()
    {
        var capture = Path.Combine(Path.GetTempPath(), $"witai_carrier_{Guid.NewGuid():N}");
        try
        {
            using var host = new WasmHost();
            using var module = WasmModule.FromBytes(host.Engine, "hello", Convert.FromBase64String(HELLO_WASM_BASE64));
            var outcome = host.Run(module, new WasmHostRunRequest
            {
                Argv = ["hello"],
                CaptureDirectory = capture,
                Fuel = 1_000_000,
                MemoryBytes = 1L << 20,
                WallMs = 10_000
            });

            return $"{PREFIX}{outcome.Verdict} {outcome.ExitCode} {outcome.Stdout.Trim()} native={WasmHostNativeLibrary.LoadedPath}";
        }
        catch (Exception exception)
        {
            return $"{PREFIX}FAILED {exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            try
            {
                if (Directory.Exists(capture))
                    Directory.Delete(capture, recursive: true);
            }
            catch (IOException)
            {
                // a temp folder; best-effort
            }
        }
    }

    #endregion
}
