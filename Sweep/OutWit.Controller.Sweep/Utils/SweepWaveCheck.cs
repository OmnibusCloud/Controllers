namespace OutWit.Controller.Sweep.Utils;

/// <summary>
/// Whether a harvested wave is exactly the chunk that was fanned out: every
/// variant of the chunk once, and nothing else. The grid returns one result
/// per task in completion order, so a count alone would pass a wave that
/// carries one variant twice and another not at all - a manifest that counts
/// the one twice and loses the other for good.
/// </summary>
public static class SweepWaveCheck
{
    #region Functions

    /// <summary>
    /// What is wrong with a wave: the chunk's variants it lacks, the variants
    /// it carries more than once, the variants it carries that the chunk does
    /// not hold.
    /// </summary>
    /// <param name="expected">The chunk's variant indices, in table order (unique: the plan refuses repeats).</param>
    /// <param name="returned">The variant indices of the wave's rows, in completion order.</param>
    /// <returns>Findings, one phrase each; empty when the wave is the chunk.</returns>
    public static IReadOnlyList<string> Findings(IReadOnlyList<int> expected, IReadOnlyList<int> returned)
    {
        var findings = new List<string>();
        var chunk = expected.ToHashSet();
        var counts = returned.GroupBy(index => index).ToDictionary(group => group.Key, group => group.Count());

        var missing = expected.Where(index => !counts.ContainsKey(index)).ToList();
        if (missing.Count > 0)
            findings.Add($"missing variant(s) {Names(missing)}");

        var repeated = counts.Where(pair => pair.Value > 1).Select(pair => pair.Key).Order().ToList();
        if (repeated.Count > 0)
            findings.Add($"variant(s) {Names(repeated)} more than once");

        var foreign = counts.Keys.Where(index => !chunk.Contains(index)).Order().ToList();
        if (foreign.Count > 0)
            findings.Add($"variant(s) {Names(foreign)} not in the chunk");

        return findings;
    }

    #endregion

    #region Tools

    private static string Names(IEnumerable<int> indices)
    {
        return string.Join(", ", indices.Select(index => $"#{index}"));
    }

    #endregion
}
