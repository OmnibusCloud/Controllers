using System.Text;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.Sweep.Utils;

/// <summary>
/// Variant deck materialization by plain placeholder substitution. The client
/// bakes the tokens into the base deck; this side never parses a deck — it
/// replaces tokens and refuses loudly when the template and the parameter
/// list disagree.
/// </summary>
public static class SweepDeckTemplating
{
    #region Constants

    private const string TOKEN_OPEN = "{{";

    #endregion

    #region Functions

    /// <summary>
    /// Verifies every parameter's token occurs in the base deck.
    /// </summary>
    /// <param name="deckText">Base deck with baked placeholder tokens.</param>
    /// <param name="parameters">The study's parameters.</param>
    /// <exception cref="InvalidOperationException">A token is empty or absent from the deck.</exception>
    public static void ValidateTemplate(string deckText, IReadOnlyList<SweepParameterData> parameters)
    {
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Token))
                throw new InvalidOperationException($"Parameter '{parameter.Name}' has no placeholder token.");

            if (!deckText.Contains(parameter.Token, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Placeholder '{parameter.Token}' of parameter '{parameter.Name}' does not occur in the base deck.");
        }
    }

    /// <summary>
    /// Produces one variant's deck text.
    /// </summary>
    /// <param name="deckText">Base deck with baked placeholder tokens.</param>
    /// <param name="parameters">The study's parameters, in token order.</param>
    /// <param name="values">The variant's substitution values, ordered like the parameters.</param>
    /// <returns>The complete variant deck.</returns>
    /// <exception cref="InvalidOperationException">Value/parameter count mismatch, or a placeholder survives substitution.</exception>
    public static string Instantiate(string deckText, IReadOnlyList<SweepParameterData> parameters, IReadOnlyList<string> values)
    {
        if (values.Count != parameters.Count)
            throw new InvalidOperationException(
                $"Variant carries {values.Count} value(s) for {parameters.Count} parameter(s).");

        var builder = new StringBuilder(deckText);
        for (var i = 0; i < parameters.Count; i++)
            builder.Replace(parameters[i].Token, values[i]);

        var result = builder.ToString();

        var leftover = result.IndexOf(TOKEN_OPEN, StringComparison.Ordinal);
        if (leftover >= 0)
        {
            var tail = result.Substring(leftover, System.Math.Min(24, result.Length - leftover)).Split('\n')[0];
            throw new InvalidOperationException($"Unsubstituted placeholder near '{tail}' — the parameter list does not cover the template.");
        }

        return result;
    }

    #endregion
}
