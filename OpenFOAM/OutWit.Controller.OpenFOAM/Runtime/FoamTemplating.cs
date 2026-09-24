using System.Text.RegularExpressions;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Token substitution in a templated case file: one pass, the longest token
/// first (so <c>{{oc10}}</c> is never mangled by <c>{{oc1}}</c>), and a check
/// that no token is left afterwards - a token without a value is a refused
/// variant, never a dictionary with braces in it.
/// </summary>
public static class FoamTemplating
{
    #region Constants

    /// <summary>The shape of a token as the initiators bake them.</summary>
    public static readonly Regex TOKEN = new(@"\{\{oc\d+\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Replaces every token that has a value.
    /// </summary>
    /// <param name="text">The templated text.</param>
    /// <param name="substitutions">The variant's values.</param>
    /// <returns>The instantiated text.</returns>
    public static string Substitute(string text, IReadOnlyList<FoamTokenValueData> substitutions)
    {
        foreach (var substitution in substitutions.OrderByDescending(entry => entry.Token.Length))
        {
            if (substitution.Token.Length == 0)
                continue;

            text = text.Replace(substitution.Token, substitution.Value, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>
    /// The tokens still present in a text after substitution.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Distinct tokens, in order of first appearance.</returns>
    public static IReadOnlyList<string> LeftoverTokens(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new List<string>();

        foreach (Match match in TOKEN.Matches(text))
        {
            if (seen.Add(match.Value))
                tokens.Add(match.Value);
        }

        return tokens;
    }

    #endregion
}
