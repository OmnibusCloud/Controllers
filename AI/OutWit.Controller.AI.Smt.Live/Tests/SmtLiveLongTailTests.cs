using System.Globalization;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests;

/// <summary>
/// The long-tail kit (<c>demo/03-long-tail</c>) without Python, a network or a solver: its
/// committed list holds the rules its selection script states (the logics, the known statuses,
/// the band of competition times, the size cap, the cap of a family, the licences, the sample), the
/// tables of its <c>EXPECTED.md</c> are the list's, and the options of a run are the same in the
/// commands of <c>SCRIPT.md</c>, in <c>check.py</c> and in the solver's sample test.
/// </summary>
[TestFixture]
public sealed class SmtLiveLongTailTests
{
    #region Constants

    private const string KIT = "AI/OutWit.Controller.AI.Smt.Live/demo/03-long-tail";
    private const string SAMPLE_TEST = "AI/OutWit.Controller.AI.Smt.Tests/Kit/SmtLongTailSampleTests.cs";

    private const int MAX_BYTES = 512 * 1024;
    private const int FAMILY_CAP = 200;
    private const double MIN_SECONDS = 1;
    private const double MAX_SECONDS = 60;
    private const int SAMPLE_PER_GROUP = 10;

    private static readonly string[] COLUMNS = ["id", "logic", "member", "bytes", "sha256", "status", "time_s", "wall_s", "time_source", "license", "category", "family", "band", "sample"];

    private static readonly Dictionary<string, string> PREFIXES = new(StringComparer.Ordinal) { ["QF_LIA"] = "lia", ["QF_BV"] = "bv" };

    private static readonly (double Low, double High, string Name)[] BANDS = [(1, 2, "1-2"), (2, 5, "2-5"), (5, 10, "5-10"), (10, 20, "10-20"), (20, 40, "20-40"), (40, 60, "40-60")];

    private static readonly string[] LICENCES =
    [
        "https://creativecommons.org/licenses/by/4.0/", "https://creativecommons.org/licenses/by-sa/4.0/",
        "https://creativecommons.org/publicdomain/zero/1.0/", "https://opensource.org/license/mit/",
        "https://www.apache.org/licenses/LICENSE-2.0", "https://www.isc.org/licenses/"
    ];

    #endregion

    #region List Tests

    [Test]
    public void TheListHoldsTheRulesOfItsSelectionTest()
    {
        var (header, rows) = List();
        var families = new Dictionary<string, int>(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(COLUMNS));
            Assert.That(rows.Select(row => row["id"]).Distinct().Count(), Is.EqualTo(rows.Count), "ids are unique");
            Assert.That(rows.Select(row => row["member"]).Distinct().Count(), Is.EqualTo(rows.Count), "paths are unique");

            foreach (var row in rows)
            {
                var id = row["id"];
                var logic = row["logic"];
                Assert.That(PREFIXES.ContainsKey(logic), Is.True, $"{id}: {logic}");
                Assert.That(id, Does.Match($"^{PREFIXES.GetValueOrDefault(logic, "?")}[0-9]{{4}}$"), id);
                Assert.That(row["member"], Does.StartWith($"non-incremental/{logic}/{row["family"]}/"), id);
                Assert.That(int.Parse(row["bytes"], CultureInfo.InvariantCulture), Is.InRange(1, MAX_BYTES), id);
                Assert.That(row["sha256"], Does.Match("^[0-9a-f]{64}$"), id);
                Assert.That(row["status"], Is.AnyOf("sat", "unsat"), id);
                var seconds = double.Parse(row["time_s"], CultureInfo.InvariantCulture);
                Assert.That(seconds, Is.InRange(MIN_SECONDS, MAX_SECONDS), id);
                Assert.That(row["band"], Is.EqualTo(Band(seconds)), id);
                Assert.That(row["time_source"], Does.Match("^SMT-COMP 20[0-9]{2} z3"), id);
                Assert.That(row["license"], Is.AnyOf(LICENCES), id);
                Assert.That(row["sample"], Is.AnyOf("0", "1"), id);
                families[$"{logic} {row["family"]}"] = families.GetValueOrDefault($"{logic} {row["family"]}") + 1;
            }

            foreach (var (family, count) in families)
                Assert.That(count, Is.LessThanOrEqualTo(FAMILY_CAP), family);

            foreach (var logic in PREFIXES.Keys)
            {
                var mine = rows.Where(row => row["logic"] == logic).ToList();
                Assert.That(mine.Select(row => row["id"]), Is.EqualTo(Enumerable.Range(0, mine.Count).Select(number => $"{PREFIXES[logic]}{number:D4}")), $"{logic}: ids in order");
                Assert.That(mine.Select(row => row["member"]), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal), $"{logic}: in the order of their paths");
                foreach (var status in new[] { "sat", "unsat" })
                    Assert.That(mine.Count(row => row["status"] == status && row["sample"] == "1"), Is.EqualTo(SAMPLE_PER_GROUP), $"{logic} {status}: the sample");
            }
        });
    }

    [Test]
    public void TheSelectionScriptStatesTheRulesTheListHoldsTest()
    {
        var script = Read("make_list.py");

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("\nMAX_BYTES = 512 << 10\n"));
            Assert.That(script, Does.Contain($"\nFAMILY_CAP = {FAMILY_CAP}\n"));
            Assert.That(script, Does.Contain("\nMIN_SECONDS = 1.0\n"));
            Assert.That(script, Does.Contain("\nMAX_SECONDS = 60.0\n"));
            Assert.That(script, Does.Contain($"\nSAMPLE_PER_GROUP = {SAMPLE_PER_GROUP}\n"));
            Assert.That(script, Does.Contain($"\nCOLUMNS = (\"{string.Join("\", \"", COLUMNS.Take(9))}\",\n"));
        });
    }

    [Test]
    public void TheTablesOfExpectedAreTheListsTest()
    {
        var (_, rows) = List();
        var expected = Read("EXPECTED.md");

        var logics = Regex.Matches(expected, @"^\| (QF_\w+) \| ([\d,]+) \| ([\d,]+) \| ([\d,]+) \| (\d+) \| ([\d,]+) \|$", RegexOptions.Multiline)
            .ToDictionary(match => match.Groups[1].Value, match => string.Join(" ", Enumerable.Range(2, 5).Select(group => match.Groups[group].Value.Replace(",", ""))));
        var listed = rows.GroupBy(row => row["logic"]).ToDictionary(group => group.Key, group => string.Join(" ",
            group.Count(), group.Count(row => row["status"] == "sat"), group.Count(row => row["status"] == "unsat"),
            group.Select(row => row["family"]).Distinct().Count(), group.Sum(row => long.Parse(row["bytes"], CultureInfo.InvariantCulture))));

        var bands = Regex.Matches(expected, @"^\| ([\d]+-[\d]+) s \| ([\d,]+) \| ([\d,]+) \| ([\d,]+) \|$", RegexOptions.Multiline)
            .ToDictionary(match => match.Groups[1].Value, match => string.Join(" ", Enumerable.Range(2, 3).Select(group => match.Groups[group].Value.Replace(",", ""))));
        var banded = BANDS.ToDictionary(band => band.Name, band => string.Join(" ",
            rows.Count(row => row["band"] == band.Name && row["logic"] == "QF_LIA"), rows.Count(row => row["band"] == band.Name && row["logic"] == "QF_BV"),
            rows.Count(row => row["band"] == band.Name)));

        Assert.Multiple(() =>
        {
            Assert.That(logics, Is.EquivalentTo(listed), "the table by logic: problems, sat, unsat, families, bytes");
            Assert.That(bands, Is.EquivalentTo(banded), "the table by band: QF_LIA, QF_BV, both");
            Assert.That(expected, Does.Contain($"{rows.Count:N0} rows"), "the set's rows");
        });
    }

    #endregion

    #region Options Tests

    [Test]
    public void TheOptionsOfARunAreTheSameInEveryPlaceTest()
    {
        var script = Read("SCRIPT.md");
        var check = Read("check.py");
        var expected = Read("EXPECTED.md");
        var sample = File.ReadAllText(Path.Combine(SmtLiveFixtures.RepositoryRoot(), SAMPLE_TEST));

        var options = Regex.Match(check, @"OPTIONS = \{""query"": ""check"", ""rlimit"": (\d+), ""hard_cap_s"": (\d+), ""memory_mb"": (\d+)\}");
        Assert.That(options.Success, Is.True, "check.py states the kit's options");
        var (rlimit, hardCap, memory) = (options.Groups[1].Value, options.Groups[2].Value, options.Groups[3].Value);
        var line = $"--rlimit {rlimit} --hard-cap {hardCap} --memory {memory}";

        var commands = Regex.Matches(script, @"smt-live (preflight|run) demo-out/long-tail/problems\.jsonl (.*)$", RegexOptions.Multiline).Select(match => match.Groups[2].Value).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(commands, Has.Count.EqualTo(3), "SCRIPT.md: the preflight and the two runs");
            Assert.That(commands, Has.All.StartWith(line), "SCRIPT.md: the options of the preflight and the runs");
            Assert.That(expected, Does.Contain($"`{line}"), "EXPECTED.md names the options");
            Assert.That(sample, Does.Contain($"RESOURCE_LIMIT = {long.Parse(rlimit, CultureInfo.InvariantCulture):#,0}".Replace(",", "_")));
            Assert.That(sample, Does.Contain($"HARD_CAP_SECONDS = {hardCap};"));
            Assert.That(sample, Does.Contain($"MEMORY_MEGABYTES = {int.Parse(memory, CultureInfo.InvariantCulture):#,0};".Replace(",", "_")));
        });
    }

    #endregion

    #region Tools

    private static string Read(string name)
    {
        // A checkout may end its lines with carriage returns.
        return File.ReadAllText(Path.Combine(SmtLiveFixtures.RepositoryRoot(), KIT, name)).ReplaceLineEndings("\n");
    }

    private static (string[] Header, List<Dictionary<string, string>> Rows) List()
    {
        var lines = Read("kit-list.tsv").Split('\n').Where(line => line.Length > 0).ToList();
        var header = lines[0].Split('\t');
        var rows = lines.Skip(1)
            .Select(line => line.Split('\t'))
            .Select(cells => header.Select((name, index) => (name, value: index < cells.Length ? cells[index] : "")).ToDictionary(pair => pair.name, pair => pair.value, StringComparer.Ordinal))
            .ToList();

        return (header, rows);
    }

    private static string Band(double seconds)
    {
        foreach (var band in BANDS)
        {
            if (seconds < band.High || band.High == MAX_SECONDS)
                return band.Name;
        }

        return BANDS[^1].Name;
    }

    #endregion
}
