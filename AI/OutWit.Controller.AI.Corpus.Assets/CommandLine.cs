using System.Globalization;

namespace OutWit.Controller.AI.Corpus.Assets;

/// <summary>
/// The arguments of a command: positional values first, then options, each option followed by
/// its values up to the next option (<c>--train a.gz b.gz --calibrate c.gz --recall 0.999</c>).
/// </summary>
internal sealed class CommandLine
{
    #region Fields

    private readonly Dictionary<string, List<string>> m_options = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>Parses the arguments after the command name.</summary>
    /// <param name="args">The arguments.</param>
    public CommandLine(IEnumerable<string> args)
    {
        List<string>? current = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                current = [];
                m_options[arg] = current;
                continue;
            }

            (current ?? Positional).Add(arg);
        }
    }

    #endregion

    #region Functions

    /// <summary>An option's values.</summary>
    /// <param name="name">The option, e.g. <c>--train</c>.</param>
    /// <returns>The values (empty when the option is absent).</returns>
    public IReadOnlyList<string> Values(string name)
    {
        return m_options.TryGetValue(name, out var values) ? values : [];
    }

    /// <summary>Whether an option is present.</summary>
    /// <param name="name">The option.</param>
    /// <returns>True when given.</returns>
    public bool Has(string name)
    {
        return m_options.ContainsKey(name);
    }

    /// <summary>An option's single value.</summary>
    /// <param name="name">The option.</param>
    /// <param name="fallback">The value when absent.</param>
    /// <returns>The value.</returns>
    public string Value(string name, string fallback)
    {
        return m_options.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : fallback;
    }

    /// <summary>An option's single whole-number value.</summary>
    /// <param name="name">The option.</param>
    /// <param name="fallback">The value when absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The value is not a whole number.</exception>
    public int Int(string name, int fallback)
    {
        var text = Value(name, "");
        if (text.Length == 0)
            return fallback;

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException($"{name} takes a whole number, not '{text}'.");
    }

    /// <summary>An option's single decimal value.</summary>
    /// <param name="name">The option.</param>
    /// <param name="fallback">The value when absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The value is not a number.</exception>
    public decimal Decimal(string name, decimal fallback)
    {
        var text = Value(name, "");
        if (text.Length == 0)
            return fallback;

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException($"{name} takes a number, not '{text}'.");
    }

    #endregion

    #region Properties

    /// <summary>The values before the first option.</summary>
    public List<string> Positional { get; } = [];

    #endregion
}
