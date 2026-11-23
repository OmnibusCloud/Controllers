using System.Globalization;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Profiles;

/// <summary>
/// The one pinned solver of this version of the controller: Z3 built for wasm32-wasip1, the module
/// by its SHA-256, and every number and setting a session runs under. Each value is part of the
/// product: the same problem under the same values gives the same outcome on every node, and a
/// changed value is another profile.
/// <para>
/// A session has two budgets of the solver's work and both are counts, not clocks: the solver's
/// own resource limit for one check, and the fuel of the whole session, which the hard cap in
/// seconds of a reference machine is turned into at <see cref="FUEL_PER_SECOND"/>. Wall time
/// never decides an outcome: the backstop (<see cref="BackstopMs"/>) protects the machine from a
/// run that neither count ends, and a run it ends says something about the machine.
/// </para>
/// </summary>
internal static class SmtProfile
{
    #region Constants

    /// <summary>The profile's id, as a run's options name it.</summary>
    public const string ID = "z3-5.4.1";

    /// <summary>The solver's version.</summary>
    public const string SOLVER_VERSION = "5.4.1";

    /// <summary>The folder of the module beside the controller.</summary>
    public const string MODULE_FOLDER = "wasm";

    /// <summary>The module's file name.</summary>
    public const string MODULE_FILE = "z3-5.4.1.wasm";

    /// <summary>
    /// SHA-256 of the solver module: Z3 5.4.1 built for wasm32-wasip1 with WebAssembly exceptions
    /// by the pinned toolchain (OutWit.Controller.AI.Smt.Assets, <c>toolchain/</c>), two
    /// independent builds byte-identical. A module with other bytes is not compiled.
    /// </summary>
    public const string MODULE_SHA256 = "9b8a67e85542b5d62d30b40060dd237fcef07f794ea235483afc83eef47a1364";

    /// <summary>The module's size, bytes.</summary>
    public const long MODULE_SIZE = 30_204_160;

    /// <summary>The solver's resource limit for one check when the options name none.</summary>
    public const long DEFAULT_RESOURCE_LIMIT = 150_000_000;

    /// <summary>
    /// The reference rate that turns a hard cap in seconds into fuel: sixty seconds are
    /// 300,000,000,000 units. A rate of the profile, not a measurement of the node: the budget
    /// is the same number everywhere, and a slower machine takes longer over it.
    /// </summary>
    public const long FUEL_PER_SECOND = 5_000_000_000;

    /// <summary>The most linear memory a wasm32 guest can have.</summary>
    public const long WASM32_MEMORY_BYTES = 1L << 32;

    /// <summary>
    /// The least the host's cap of the linear memory stands above the solver's own ceiling. The
    /// linear memory holds what the solver does not count: its stack of 8 MiB, its data, what
    /// the allocator keeps for itself (an idle solver counts 8.6 MB of its own and has 18 MiB of
    /// linear memory). Under a small ceiling a quarter is less than that.
    /// </summary>
    public const long HOST_MEMORY_HEADROOM_BYTES = 16L << 20;

    /// <summary>The most one session may write to its stdout, all its answers together; the write that passes it ends the session.</summary>
    public const int OUTPUT_LIMIT_BYTES = 16 << 20;

    /// <summary>The most of a session's stderr that is kept; the rest is dropped and the session goes on.</summary>
    public const int ERROR_OUTPUT_LIMIT_BYTES = 64 << 10;

    /// <summary>The wall time every session has before its fuel is counted in: start-up on a machine that is busy.</summary>
    public const int BACKSTOP_BASE_MS = 60_000;

    /// <summary>How many times slower than the reference rate a machine may burn its fuel before the backstop ends a session.</summary>
    public const int BACKSTOP_SLOWDOWN = 10;

    /// <summary>The line the solver writes to stderr when it ends for want of memory, with its exit code <see cref="MEMORY_EXIT_CODE"/>.</summary>
    public const string OUT_OF_MEMORY_MARKER = "(error \"out of memory\")";

    /// <summary>The exit code the solver ends with when it is refused memory, by its own ceiling or by the host's cap.</summary>
    public const int MEMORY_EXIT_CODE = 101;

    private const string GUEST_NAME = "z3";
    private const string ARGUMENT_STDIN = "-in";
    private const string ARGUMENT_SMT2 = "-smt2";
    private const string ARGUMENT_MEMORY = "-memory:";

    private const string OPTION_PRINT_SUCCESS = ":print-success";
    private const string OPTION_PRODUCE_MODELS = ":produce-models";
    private const string OPTION_SMT_SEED = ":smt.random_seed";
    private const string OPTION_SAT_SEED = ":sat.random_seed";
    private const string TRUE = "true";
    private const string FALSE = "false";

    private const long BYTES_PER_MEGABYTE = 1L << 20;
    private const int MILLISECONDS_PER_SECOND = 1_000;

    #endregion

    #region Functions

    /// <summary>
    /// Whether an id of a run's options names this profile.
    /// </summary>
    /// <param name="id">The id; empty for the controller's default.</param>
    /// <returns>True for the empty id and for <see cref="ID"/>.</returns>
    public static bool Is(string? id)
    {
        return string.IsNullOrEmpty(id) || string.Equals(id, ID, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fuel of a session: the hard cap at the reference rate.
    /// </summary>
    /// <param name="hardCapSeconds">The hard cap, seconds of a reference machine.</param>
    /// <returns>The fuel, at least one second's.</returns>
    public static long Fuel(int hardCapSeconds)
    {
        return Math.Max(1, hardCapSeconds) * FUEL_PER_SECOND;
    }

    /// <summary>
    /// The host's cap of the guest's linear memory for a memory option. The solver counts what
    /// it allocates and ends by itself at its own ceiling, which is the option's megabytes; the
    /// linear memory is always larger than that count, so the host's cap is a quarter above the
    /// ceiling, and <see cref="HOST_MEMORY_HEADROOM_BYTES"/> above it at the least: the solver
    /// meets its own ceiling first and ends cleanly, and under a ceiling too small for it to
    /// start in it still starts, and ends by that ceiling at once. Never more than a wasm32 guest
    /// can address.
    /// </summary>
    /// <param name="megabytes">The solver's ceiling, megabytes.</param>
    /// <returns>The cap, bytes.</returns>
    public static long HostMemoryBytes(int megabytes)
    {
        var ceiling = Math.Max(1, megabytes) * BYTES_PER_MEGABYTE;
        return Math.Min(WASM32_MEMORY_BYTES, ceiling + Math.Max(ceiling / 4, HOST_MEMORY_HEADROOM_BYTES));
    }

    /// <summary>
    /// The wall-clock backstop of a session with a given fuel: <see cref="BACKSTOP_BASE_MS"/> and
    /// the time the fuel takes on a machine <see cref="BACKSTOP_SLOWDOWN"/> times slower than the
    /// reference rate. It is no budget of the problem's: fuel ends a session first on any machine
    /// that can run a node.
    /// </summary>
    /// <param name="fuel">The session's fuel.</param>
    /// <returns>The backstop, milliseconds.</returns>
    public static int BackstopMs(long fuel)
    {
        var burn = Math.Max(0, fuel) / (FUEL_PER_SECOND / MILLISECONDS_PER_SECOND) * BACKSTOP_SLOWDOWN;
        return (int)Math.Min(int.MaxValue, BACKSTOP_BASE_MS + burn);
    }

    /// <summary>
    /// The envelope of a session under a run's options: what is not set (0) is the default, the
    /// model package's for the hard cap, the memory and the number of models, the profile's for
    /// the resource limit.
    /// </summary>
    /// <param name="options">The options, each in its range (<see cref="SmtOptionsRules.Check"/>).</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="ArgumentNullException">There are no options.</exception>
    public static SmtEnvelope Envelope(SmtOptionsData options)
    {
        var resolved = SmtOptionsRules.Resolve(options);
        var fuel = Fuel(resolved.HardCapSeconds);

        return new SmtEnvelope(
            ResourceLimit: resolved.ResourceLimit == 0 ? DEFAULT_RESOURCE_LIMIT : resolved.ResourceLimit,
            Fuel: fuel,
            MemoryMegabytes: resolved.MemoryMegabytes,
            HostMemoryBytes: HostMemoryBytes(resolved.MemoryMegabytes),
            OutputLimitBytes: OUTPUT_LIMIT_BYTES,
            BackstopMs: BackstopMs(fuel),
            Seed: resolved.Seed,
            MaxModels: resolved.MaxModels);
    }

    /// <summary>
    /// The solver's command line, <c>argv[0]</c> included: SMT-LIB from stdin, and its own memory
    /// ceiling.
    /// </summary>
    /// <param name="envelope">The session's envelope.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> Arguments(SmtEnvelope envelope)
    {
        return [GUEST_NAME, ARGUMENT_STDIN, ARGUMENT_SMT2, ARGUMENT_MEMORY + envelope.MemoryMegabytes.ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>
    /// The options the controller sets before a problem: no success lines, models on, and the
    /// seed of the solver's random choices.
    /// </summary>
    /// <param name="seed">The seed.</param>
    /// <returns>The commands, in the order they are sent.</returns>
    public static IReadOnlyList<SmtExpression> Options(int seed)
    {
        var number = SmtExpression.Atom(SmtExpressionKind.Numeral, seed.ToString(CultureInfo.InvariantCulture));

        return
        [
            SmtScripts.SetOption(OPTION_PRINT_SUCCESS, SmtExpression.Symbol(FALSE)),
            SmtScripts.SetOption(OPTION_PRODUCE_MODELS, SmtExpression.Symbol(TRUE)),
            SmtScripts.SetOption(OPTION_SMT_SEED, number),
            SmtScripts.SetOption(OPTION_SAT_SEED, number)
        ];
    }

    #endregion
}
