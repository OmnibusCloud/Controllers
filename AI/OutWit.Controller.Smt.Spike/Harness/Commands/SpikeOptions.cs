namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>The envelope of a run: the solver's budget and limits, the same for wasm and native.</summary>
internal sealed class SpikeOptions
{
    #region Constants

    /// <summary>The wasm32 ceiling: 65,536 pages of 64 KiB.</summary>
    public const long WASM32_MAX_BYTES = 4L << 30;

    #endregion

    #region Functions

    /// <summary>Reads the options shared by the verbs.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The options.</returns>
    public static SpikeOptions From(SpikeArgs args)
    {
        return new SpikeOptions
        {
            Rlimit = args.Long("rlimit", 2_000_000_000),
            Fuel = args.Long("fuel", 500_000_000_000),
            MemoryMb = (int)args.Long("memory-mb", 1024),
            WallMs = (int)args.Long("wall-ms", 180_000),
            Seed = args.Long("seed", 0),
            StdoutLimit = (int)args.Long("stdout-limit", 16 << 20)
        };
    }

    /// <summary>The solver's command line (argv[0] included), the same for wasm and native: interactive SMT-LIB, fixed seeds, Z3's own memory ceiling.</summary>
    /// <returns>argv.</returns>
    public List<string> Argv()
    {
        return ["z3", "-in", "-smt2", $"smt.random_seed={Seed}", $"sat.random_seed={Seed}", $"-memory:{MemoryMb}"];
    }

    #endregion

    #region Properties

    /// <summary>The resource limit per check (Z3's rlimit; 32-bit in Z3).</summary>
    public long Rlimit { get; init; }

    /// <summary>The guest's fuel budget (wasm only).</summary>
    public long Fuel { get; init; }

    /// <summary>Z3's memory ceiling (-memory:, MB); the wasm cap is a quarter more, at most the wasm32 ceiling.</summary>
    public int MemoryMb { get; init; }

    /// <summary>The wall-clock backstop per session.</summary>
    public int WallMs { get; init; }

    /// <summary>The solver's seeds (smt and sat) and the guest's random_get seed.</summary>
    public long Seed { get; init; }

    /// <summary>The most stdout bytes kept per session.</summary>
    public int StdoutLimit { get; init; }

    /// <summary>The cap of the guest's linear memory.</summary>
    public long WasmMemoryBytes => Math.Min(WASM32_MAX_BYTES, (long)MemoryMb * (1 << 20) * 5 / 4);

    #endregion
}
