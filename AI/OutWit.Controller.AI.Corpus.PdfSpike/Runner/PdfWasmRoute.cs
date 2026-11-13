namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>The wasm route: which wasmtime runs which precompiled extractor under which limits.</summary>
/// <param name="Wasmtime">The wasmtime command line.</param>
/// <param name="Module">The precompiled module (wasmtime compile output).</param>
/// <param name="Fuel">The fuel per document.</param>
/// <param name="MaxMemoryBytes">The linear-memory ceiling per document.</param>
public sealed record PdfWasmRoute(string Wasmtime, string Module, long Fuel, long MaxMemoryBytes);
