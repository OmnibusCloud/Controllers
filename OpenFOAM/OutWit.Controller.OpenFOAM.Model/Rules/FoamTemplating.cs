using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// Token substitution in a case's templated files, and the rules that make
/// it safe: one pass over the text, whole tokens only (so <c>{{oc10}}</c> is
/// never mangled by <c>{{oc1}}</c>, and a value is never substituted into
/// again); a variant's values name tokens of the token shape, each once; a
/// token left after substitution refuses the variant, never a dictionary with
/// braces in it; and before a study starts,
/// every token it declares occurs in some templated file and every token of
/// the templated files is declared. The node substitutes; the Sweep host and
/// the initiator check coverage with the same grammar.
/// </summary>
public static class FoamTemplating
{
    #region Constants

    /// <summary>The shape of a token as the initiators bake them.</summary>
    public static readonly Regex TOKEN = new(@"\{\{oc\d+\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WHOLE_TOKEN = new(@"^\{\{oc\d+\}\}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Replaces every token that has a value, in one pass over the text: each
    /// token found is replaced by its value, and a value is never scanned
    /// again - a value that happens to contain a token stays as it is. A token
    /// without a value is left in place for <see cref="LeftoverTokens"/> to
    /// name.
    /// </summary>
    /// <param name="text">The templated text.</param>
    /// <param name="substitutions">The variant's values; the first value of a token counts (<see cref="CheckSubstitutions"/> refuses a second).</param>
    /// <returns>The instantiated text.</returns>
    public static string Substitute(string text, IReadOnlyList<FoamTokenValueData> substitutions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var substitution in substitutions)
            values.TryAdd(substitution.Token, substitution.Value);

        if (values.Count == 0)
            return text;

        return TOKEN.Replace(text, match => values.TryGetValue(match.Value, out var value) ? value : match.Value);
    }

    /// <summary>
    /// Checks a variant's values before they are applied: every token has the
    /// token shape (a value for anything else would never be substituted) and
    /// no token has two values.
    /// </summary>
    /// <param name="substitutions">The variant's values.</param>
    /// <returns>Findings, one sentence each; empty when the values may be applied.</returns>
    public static IReadOnlyList<string> CheckSubstitutions(IReadOnlyList<FoamTokenValueData> substitutions)
    {
        var findings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var substitution in substitutions)
        {
            if (!WHOLE_TOKEN.IsMatch(substitution.Token))
                findings.Add($"'{substitution.Token}' is not a token of the form {{{{ocN}}}}; its value would never be substituted.");
            else if (!seen.Add(substitution.Token))
                findings.Add($"Token {substitution.Token} has more than one value in this variant.");
        }

        return findings;
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

    /// <summary>
    /// Checks a study's tokens against the case's templated files: each
    /// declared token has the token shape and occurs in at least one templated
    /// file, and each token a templated file carries is declared - otherwise
    /// every variant would be refused on its node, one after another.
    /// </summary>
    /// <param name="tokens">The study's declared tokens.</param>
    /// <param name="templatedFiles">Every templated file of the case: its relative path and its text.</param>
    /// <returns>Findings, one sentence each; empty when every variant can be instantiated.</returns>
    public static IReadOnlyList<string> CheckCoverage(IReadOnlyList<string> tokens, IReadOnlyList<(string RelativePath, string Text)> templatedFiles)
    {
        var findings = new List<string>();
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (!WHOLE_TOKEN.IsMatch(token))
            {
                findings.Add($"'{token}' is not a token of the form {{{{ocN}}}}.");
                continue;
            }

            if (!declared.Add(token))
            {
                findings.Add($"Token {token} is declared twice.");
                continue;
            }

            if (templatedFiles.All(file => !file.Text.Contains(token, StringComparison.Ordinal)))
                findings.Add($"Token {token} occurs in no templated file of the case.");
        }

        foreach (var (relativePath, text) in templatedFiles)
        {
            foreach (var token in LeftoverTokens(text).Where(token => !declared.Contains(token)))
                findings.Add($"{relativePath}: token {token} is not declared by the study.");
        }

        return findings;
    }

    #endregion
}
