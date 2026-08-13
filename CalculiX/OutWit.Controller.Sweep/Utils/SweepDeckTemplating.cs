using System.Text.RegularExpressions;
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

        // ONE pass over the deck, all tokens at once: sequential Replace
        // calls would re-scan each parameter's OUTPUT, so a substituted
        // value containing a later token got silently re-substituted.
        // Longest token first (regex alternation is first-match); the first
        // parameter claiming a token wins, matching the old semantics.
        var byToken = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
            byToken.TryAdd(parameters[i].Token, values[i]);

        var result = parameters.Count == 0
            ? deckText
            : Regex.Replace(
                deckText,
                string.Join("|", byToken.Keys.OrderByDescending(token => token.Length).Select(Regex.Escape)),
                match => byToken[match.Value]);

        ThrowOnLeftoverPlaceholder(result);
        return result;
    }

    // A surviving "{{" outside a comment means the parameter list does not
    // cover the template — but a literal "{{" INSIDE a deck comment is the
    // deck author's own business, and failing every variant over it would
    // punish a remark (** lines are comments in ccx).
    private static void ThrowOnLeftoverPlaceholder(string result)
    {
        var search = 0;
        while (search < result.Length)
        {
            var leftover = result.IndexOf(TOKEN_OPEN, search, StringComparison.Ordinal);
            if (leftover < 0)
                return;

            var lineStart = result.LastIndexOf('\n', leftover) + 1;
            var probe = lineStart;
            while (probe < result.Length && result[probe] is ' ' or '\t')
                probe++;

            var isComment = probe + 1 < result.Length && result[probe] == '*' && result[probe + 1] == '*';
            if (!isComment)
            {
                var tail = result.Substring(leftover, System.Math.Min(24, result.Length - leftover)).Split('\n')[0];
                throw new InvalidOperationException(
                    $"Unsubstituted placeholder near '{tail}' — the parameter list does not cover the template.");
            }

            search = leftover + TOKEN_OPEN.Length;
        }
    }

    #endregion
}
