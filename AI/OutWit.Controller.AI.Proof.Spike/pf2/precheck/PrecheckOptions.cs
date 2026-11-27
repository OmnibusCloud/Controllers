namespace OutWit.Controller.AI.Proof.Spike.Precheck;

/// <summary>
/// The limits and expectations of one pre-check run (spec sections 5.1 and 5.4).
/// </summary>
public sealed class PrecheckOptions
{
    #region Constants

    private const long DEFAULT_MAX_BYTES = 16L << 30;

    private const long DEFAULT_MAX_LINES = 300_000_000;

    private const int DEFAULT_MAX_LINE_BYTES = 16 << 20;

    private const int DEFAULT_MAX_LITERAL_DIGITS = 100_000;

    private const int DEFAULT_MAX_JSON_DEPTH = 16;

    #endregion

    #region Functions

    /// <summary>
    /// Reads the options from the command line after the file name.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">An unknown option or a missing value.</exception>
    public static PrecheckOptions Parse(IReadOnlyList<string> args)
    {
        var options = new PrecheckOptions();
        for (var i = 0; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

            switch (args[i])
            {
                case "--lean-version": options.LeanVersion = Value(); break;
                case "--lean-githash": options.LeanGithash = Value(); break;
                case "--theorems": options.Theorems = Value().Split(',', StringSplitOptions.RemoveEmptyEntries); break;
                case "--no-duplicates": options.CheckDuplicates = false; break;
                case "--sha256": options.ComputeSha256 = true; break;
                case "--expect-sha256": options.ExpectedSha256 = Value().ToLowerInvariant(); options.ComputeSha256 = true; break;
                case "--max-bytes": options.MaxBytes = long.Parse(Value()); break;
                case "--max-lines": options.MaxLines = long.Parse(Value()); break;
                case "--max-line-bytes": options.MaxLineBytes = int.Parse(Value()); break;
                case "--max-literal-digits": options.MaxLiteralDigits = int.Parse(Value()); break;
                case "--max-json-depth": options.MaxJsonDepth = int.Parse(Value()); break;
                case "--no-stop": options.StopAtFirst = false; break;
                case "--max-term-depth": options.MaxTermDepth = int.Parse(Value()); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }
        return options;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The Lean version the manifest declares; the meta line must say the same (null: not checked).
    /// </summary>
    public string? LeanVersion { get; private set; }

    /// <summary>
    /// The Lean git hash the manifest declares (null: not checked).
    /// </summary>
    public string? LeanGithash { get; private set; }

    /// <summary>
    /// The manifest's theorems; each must be declared as a theorem in the export.
    /// </summary>
    public IReadOnlyList<string> Theorems { get; private set; } = [];

    /// <summary>
    /// Whether repeated entries are looked for (costs a fingerprint per entry).
    /// </summary>
    public bool CheckDuplicates { get; private set; } = true;

    /// <summary>
    /// Whether the SHA-256 of the (decompressed) bytes is computed in the same pass.
    /// </summary>
    public bool ComputeSha256 { get; private set; }

    /// <summary>
    /// The digest the bytes must have (null: not compared).
    /// </summary>
    public string? ExpectedSha256 { get; private set; }

    /// <summary>
    /// The ceiling on the export's (decompressed) bytes.
    /// </summary>
    public long MaxBytes { get; private set; } = DEFAULT_MAX_BYTES;

    /// <summary>
    /// The ceiling on the export's lines.
    /// </summary>
    public long MaxLines { get; private set; } = DEFAULT_MAX_LINES;

    /// <summary>
    /// The longest line allowed, in bytes.
    /// </summary>
    public int MaxLineBytes { get; private set; } = DEFAULT_MAX_LINE_BYTES;

    /// <summary>
    /// The most digits a number literal may have.
    /// </summary>
    public int MaxLiteralDigits { get; private set; } = DEFAULT_MAX_LITERAL_DIGITS;

    /// <summary>
    /// The deepest JSON nesting allowed in a line.
    /// </summary>
    public int MaxJsonDepth { get; private set; } = DEFAULT_MAX_JSON_DEPTH;

    /// <summary>
    /// The deepest term allowed (0: measured, not limited).
    /// </summary>
    public int MaxTermDepth { get; private set; }

    /// <summary>
    /// Whether the run stops at the first refusal (the controller's behaviour).
    /// </summary>
    public bool StopAtFirst { get; private set; } = true;

    #endregion
}
