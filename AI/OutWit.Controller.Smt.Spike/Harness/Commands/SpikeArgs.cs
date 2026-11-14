using System.Globalization;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>The command line of the harness: a verb, then <c>--name value</c> pairs and <c>--flag</c>s.</summary>
internal sealed class SpikeArgs
{
    #region Fields

    private readonly Dictionary<string, string> m_values = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>Reads the arguments after the verb.</summary>
    /// <param name="args">The process's arguments.</param>
    public SpikeArgs(string[] args)
    {
        Verb = args.Length > 0 ? args[0] : "";
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;

            var name = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            m_values[name] = hasValue ? args[++i] : "true";
        }
    }

    #endregion

    #region Functions

    /// <summary>A required text value.</summary>
    /// <param name="name">The option's name without the dashes.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The option is missing.</exception>
    public string Required(string name)
    {
        return m_values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"--{name} is required");
    }

    /// <summary>An optional text value.</summary>
    /// <param name="name">The option's name.</param>
    /// <returns>The value, or null.</returns>
    public string? Optional(string name)
    {
        return m_values.GetValueOrDefault(name);
    }

    /// <summary>An optional integer value.</summary>
    /// <param name="name">The option's name.</param>
    /// <param name="fallback">The value when missing.</param>
    /// <returns>The value.</returns>
    public long Long(string name, long fallback)
    {
        return m_values.TryGetValue(name, out var value) ? (long)double.Parse(value, CultureInfo.InvariantCulture) : fallback;
    }

    /// <summary>Whether a flag is given.</summary>
    /// <param name="name">The flag's name.</param>
    /// <returns>True when given.</returns>
    public bool Flag(string name)
    {
        return m_values.ContainsKey(name);
    }

    #endregion

    #region Properties

    /// <summary>The verb: wasm, native, limits, evaluate, selftest.</summary>
    public string Verb { get; }

    #endregion
}
