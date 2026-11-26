using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoryPack;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Diagnostics;
using OutWit.Controller.AI.Smt.Documents;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// The documents an application author reads, held to the code: every table that states a number,
/// a word or a name of the code (the option table, the limits of capabilities.md, the profile of
/// the README, the tables of the application page, the option table of the live tool) is read from
/// the document and each cell held to the constant it states, and the version lines to the three
/// project files. A number moved in the code, or a word added to a list, fails here until the
/// document says it. When a version moves, every line that names it fails by its file.
/// </summary>
[TestFixture]
public sealed class SmtDocumentsTests
{
    #region Constants

    private const string CONTROLLER_PROJECT = "AI/OutWit.Controller.AI.Smt/OutWit.Controller.AI.Smt.csproj";
    private const string MODEL_PROJECT = "AI/OutWit.Controller.AI.Smt.Model/OutWit.Controller.AI.Smt.Model.csproj";
    private const string SCRIPTS_PROJECT = "AI/OutWit.Controller.AI.Smt.Scripts/OutWit.Controller.AI.Smt.Scripts.csproj";
    private const string MODEL_PROPS = "Build/OutWit.Controller.Model.props";

    private const string README = "AI/OutWit.Controller.AI.Smt/README.md";
    private const string CAPABILITIES = "AI/OutWit.Controller.AI.Smt/docs/capabilities.md";
    private const string APPLICATION = "AI/OutWit.Controller.AI.Smt/docs/application.md";
    private const string MODEL_README = "AI/OutWit.Controller.AI.Smt.Model/README.md";
    private const string SCRIPTS_README = "AI/OutWit.Controller.AI.Smt.Scripts/README.md";
    private const string LIVE_README = "AI/OutWit.Controller.AI.Smt.Live/README.md";
    private const string ROOT_README = "README.md";

    private const long MIB = 1L << 20;
    private const long GIB = 1L << 30;

    #endregion

    #region Version Tests

    [Test]
    public void VersionLinesAreTheProjectsTest()
    {
        var controller = ProjectVersion(CONTROLLER_PROJECT);
        var model = ProjectVersion(MODEL_PROJECT);
        var scripts = ProjectVersion(SCRIPTS_PROJECT);
        const string RELEASE = "\\*\\*Smt (?<c>[0-9][^*]*)\\*\\* \\(with Smt\\.Model (?<m>[0-9][^ ]*) and Smt\\.Scripts (?<s>[0-9][^)]*)\\)";

        Assert.Multiple(() =>
        {
            Assert.That(ControllerBuildInfo.VERSION, Is.EqualTo(controller), "the version the controller is built with is the project's");

            AssertLine(README, "^\\*\\*Status: (?<c>[0-9][^,*]*),", ("c", controller));
            AssertLine(CAPABILITIES, "^Version described: " + RELEASE, ("c", controller), ("m", model), ("s", scripts));
            AssertLine(APPLICATION, "^Written for " + RELEASE, ("c", controller), ("m", model), ("s", scripts));
            AssertLine(MODEL_README, "This page describes \\*\\*(?<m>[0-9][^*]*)\\*\\*", ("m", model));
            AssertLine(SCRIPTS_README, "^\\*\\*Status: (?<s>[0-9][^,*]*),", ("s", scripts));

            var packages = SmtMarkdown.Table(SmtMarkdown.Read(ROOT_README), "## Published controllers");
            foreach (var (package, version) in new[] { ("OutWit.Controller.AI.Smt", controller), ("OutWit.Controller.AI.Smt.Model", model), ("OutWit.Controller.AI.Smt.Scripts", scripts) })
            {
                var rows = packages.Rows.Where(row => SmtMarkdown.Codes(row[0]).FirstOrDefault() == package).ToList();
                Assert.That(rows, Has.Count.EqualTo(1), $"{ROOT_README}: a row of {package}");
                if (rows.Count == 1)
                    Assert.That(rows[0][1], Does.StartWith(version + " "), $"{ROOT_README}: the version of {package} is its project's; move the row with the project");
            }
        });
    }

    [Test]
    public void TheModelPackageIsWhatThePageSaysTest()
    {
        var props = SmtMarkdown.Read(MODEL_PROPS);
        var framework = Regex.Match(props, "<TargetFramework>(?<f>[^<]+)</TargetFramework>").Groups["f"].Value;
        var references = Regex.Matches(props, "<PackageReference Include=\"(?<name>[^\"]+)\" Version=\"(?<version>[^\"]+)\"\\s*/>")
            .Select(match => $"`{match.Groups["name"].Value}` {match.Groups["version"].Value}").ToList();
        var page = Flat(SmtMarkdown.Read(APPLICATION));

        Assert.Multiple(() =>
        {
            Assert.That(SmtMarkdown.Read(MODEL_PROJECT), Does.Not.Contain("<PackageReference"), "the model package depends on what the shared props give it, and on nothing else");
            Assert.That(references, Has.Count.EqualTo(2), "the dependencies of a model package that the page names");
            Assert.That(page, Does.Contain($"The package is built for `{framework}` and depends on {string.Join(" and ", references)}."));
        });
    }

    #endregion

    #region Option Tests

    [Test]
    public void TheOptionTableIsTheRulesTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(APPLICATION), "### The options");
        var properties = typeof(SmtOptionsData).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(property => property.Name);
        var defaults = SmtOptionsRules.Resolve(new SmtOptionsData());
        var names = Capabilities().GetProperty("defaults").EnumerateObject().Select(property => property.Name).Prepend("query").Prepend("profile").ToList();

        // The option, its name, its least value, what 0 means, its default, its greatest, and the sentence it is refused with.
        var expected = new Dictionary<string, string[]>
        {
            ["Profile"] = ["`profile`", "", "the controller's profile", $"`{SmtProfile.ID}`", "", $"`{ProfileSentence()}`"],
            ["Query"] = ["`query`", "", "`Check` (the enum's 0)", "`check`", "", $"`{Refusal(new SmtOptionsData { Query = (SmtQuery)3 })}`"],
            ["MaxModels"] = ["`max_models`", "1", "the default", Number(defaults.MaxModels), Number(SmtOptionsRules.MAX_MODELS), $"`{Refusal(new SmtOptionsData { MaxModels = -1 })}`"],
            ["Seed"] = ["`seed`", "0", "a seed like the others, and the default", Number(defaults.Seed), Number(SmtOptionsRules.MAX_SEED), $"`{Refusal(new SmtOptionsData { Seed = -1 })}`"],
            ["ResourceLimit"] = ["`rlimit`", "1", "the profile's", Number(SmtProfile.DEFAULT_RESOURCE_LIMIT), Number(SmtOptionsRules.MAX_RESOURCE_LIMIT), $"`{Refusal(new SmtOptionsData { ResourceLimit = -1 })}`"],
            ["HardCapSeconds"] = ["`hard_cap_s`", "1", "the default", Number(defaults.HardCapSeconds), Number(SmtOptionsRules.MAX_HARD_CAP_SECONDS), $"`{Refusal(new SmtOptionsData { HardCapSeconds = -1 })}`"],
            ["MemoryMegabytes"] = ["`memory_mb`", "1", "the default", Number(defaults.MemoryMegabytes), Number(SmtOptionsRules.MAX_MEMORY_MEGABYTES), $"`{Refusal(new SmtOptionsData { MemoryMegabytes = -1 })}`"],
            ["SamplingRate"] = ["`sampling_rate`", "0", "no package solved twice: a rate of its own", SmtMarkdown.Plain(defaults.SamplingRate), SmtMarkdown.Plain(SmtOptionsRules.MAX_SAMPLING_RATE), $"`{Refusal(new SmtOptionsData { SamplingRate = -0.01 })}`"]
        };

        Assert.Multiple(() =>
        {
            Assert.That(table.Rows.Select(row => SmtMarkdown.Codes(row[0]).Single()), Is.EquivalentTo(properties), "a row for every option, and none beside them");
            Assert.That(table.Column(1).Select(name => name.Trim('`')), Is.EquivalentTo(names), "named as the documents name them");
            foreach (var (option, cells) in expected)
                Assert.That(table.Row($"`{option}`").Skip(1), Is.EqualTo(cells), option);
        });
    }

    [Test]
    public void TheLeastValueAndZeroAreWhatTheRulesDoTest()
    {
        // The least value of a number is taken and the one below it refused; 0 is the default but
        // where it is a value of its own.
        Assert.Multiple(() =>
        {
            Assert.That(SmtOptionsRules.Check(new SmtOptionsData { MaxModels = 1, Seed = 0, ResourceLimit = 1, HardCapSeconds = 1, MemoryMegabytes = 1, SamplingRate = 0 }), Is.Empty);
            Assert.That(SmtOptionsRules.Resolve(new SmtOptionsData()).MaxModels, Is.EqualTo(SmtOptionsRules.DEFAULT_MAX_MODELS));
            Assert.That(SmtOptionsRules.Resolve(new SmtOptionsData()).HardCapSeconds, Is.EqualTo(SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS));
            Assert.That(SmtOptionsRules.Resolve(new SmtOptionsData()).MemoryMegabytes, Is.EqualTo(SmtOptionsRules.DEFAULT_MEMORY_MEGABYTES));
            Assert.That(SmtProfile.Envelope(new SmtOptionsData()).ResourceLimit, Is.EqualTo(SmtProfile.DEFAULT_RESOURCE_LIMIT), "0 is the profile's resource limit");
            Assert.That(SmtProfile.Envelope(new SmtOptionsData()).Seed, Is.Zero, "0 is a seed, and the default one");
            Assert.That(SmtRunRig.Selected(100, 0), Is.Zero, "a rate of 0 asks for no package twice");
            Assert.That(new SmtOptionsData().SamplingRate, Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE), "a new instance holds the default rate");
            Assert.That(SmtProfile.Is(""), Is.True, "an empty profile is the controller's");
        });
    }

    [Test]
    public void TheLiveToolsOptionTableIsTheRulesTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(LIVE_README), "## The options of a run");
        var defaults = SmtOptionsRules.Resolve(new SmtOptionsData());

        // The name, then what an option not given is, what 0 is, and the greatest.
        var expected = new Dictionary<string, string[]>
        {
            ["query"] = ["`check`", "", ""],
            ["sampling_rate"] = [SmtMarkdown.Plain(defaults.SamplingRate), "no copies", SmtMarkdown.Plain(SmtOptionsRules.MAX_SAMPLING_RATE)],
            ["rlimit"] = [$"{Number(SmtProfile.DEFAULT_RESOURCE_LIMIT)} (the profile's)", "the default", Number(SmtOptionsRules.MAX_RESOURCE_LIMIT)],
            ["hard_cap_s"] = [Number(defaults.HardCapSeconds), "the default", Number(SmtOptionsRules.MAX_HARD_CAP_SECONDS)],
            ["memory_mb"] = [Number(defaults.MemoryMegabytes), "the default", Number(SmtOptionsRules.MAX_MEMORY_MEGABYTES)],
            ["seed"] = [Number(defaults.Seed), "the default", Number(SmtOptionsRules.MAX_SEED)],
            ["max_models"] = [Number(defaults.MaxModels), "the default", Number(SmtOptionsRules.MAX_MODELS)]
        };

        Assert.Multiple(() =>
        {
            Assert.That(table.Column(1).Select(name => name.Trim('`')), Is.EquivalentTo(expected.Keys), "every option the tool takes, by its name");
            foreach (var row in table.Rows)
                Assert.That(row.Skip(2), Is.EqualTo(expected[row[1].Trim('`')]), row[0]);
        });
    }

    #endregion

    #region Limit Tests

    [Test]
    public void TheProfileTableIsTheProfileTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(README), "## The profile");
        var asset = DataAsset("smt-z3-wasm");
        var hardCap = SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS * SmtProfile.FUEL_PER_SECOND;

        Assert.Multiple(() =>
        {
            Assert.That(table.Column(0), Is.EquivalentTo(new[] { "Id", "Solver", "Module", "Resource limit of one check", "Hard cap", "Memory", "Output", "Wall clock" }));
            Assert.That(table.Row("Id")[1], Is.EqualTo($"`{SmtProfile.ID}`"));
            Assert.That(table.Row("Solver")[1], Does.StartWith($"Z3 {SmtProfile.SOLVER_VERSION} built for `wasm32-wasip1`"));

            var module = table.Row("Module")[1];
            Assert.That(module, Does.StartWith($"`{SmtProfile.MODULE_FOLDER}/{SmtProfile.MODULE_FILE}`, {Number(SmtProfile.MODULE_SIZE)} bytes, SHA-256 `{SmtProfile.MODULE_SHA256}`"));
            Assert.That(module, Does.Contain($"the archive `{asset.File}` of the release `{asset.Release}` ({Number(asset.Size)} bytes, SHA-256 `{asset.Sha256}`"));

            Assert.That(table.Row("Resource limit of one check")[1], Does.StartWith($"{Number(SmtProfile.DEFAULT_RESOURCE_LIMIT)} units").And.EndWith($"at most {Number(SmtOptionsRules.MAX_RESOURCE_LIMIT)}"));
            Assert.That(table.Row("Hard cap")[1], Does.Contain($"at {Number(SmtProfile.FUEL_PER_SECOND)} units a second: {SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS} s are {Power(hardCap)} units"));

            Assert.That(SmtProfile.HostMemoryBytes(1_024), Is.EqualTo(1_024 * MIB * 5 / 4), "the cap is a quarter above the solver's ceiling");
            Assert.That(SmtProfile.HostMemoryBytes(1), Is.EqualTo(MIB + SmtProfile.HOST_MEMORY_HEADROOM_BYTES), "and the headroom above it at the least");
            Assert.That(table.Row("Memory")[1], Does.Contain($"the sandbox's cap of its linear memory is a quarter above it, and {SmtProfile.HOST_MEMORY_HEADROOM_BYTES / MIB} MiB above it at the least"));

            Assert.That(table.Row("Output")[1], Does.StartWith($"{SmtProfile.OUTPUT_LIMIT_BYTES / MIB} MiB for everything one session writes"));
            Assert.That((SmtProfile.BACKSTOP_BASE_MS, SmtProfile.BACKSTOP_SLOWDOWN), Is.EqualTo((60_000, 10)), "the words of the wall clock's row");
            Assert.That(table.Row("Wall clock")[1], Does.Contain("a minute and ten times the time the fuel takes at the reference rate"));
        });
    }

    [Test]
    public void TheSessionNumbersOfTheReadmeAreTheDegreeRuleTest()
    {
        var readme = Flat(SmtMarkdown.Read(README));
        var session = SmtSandboxDegree.SessionBytes(new SmtOptionsData());
        var needs = (long)Math.Ceiling(session / SmtSandboxDegree.RAM_HEADROOM / MIB);
        int Fit(long gibibytes) => (int)(gibibytes * GIB * SmtSandboxDegree.RAM_HEADROOM / session);
        var words = new Dictionary<int, string> { [1] = "one", [4] = "four", [16] = "sixteen", [17] = "seventeen" };

        Assert.Multiple(() =>
        {
            Assert.That(session % MIB, Is.Zero);
            Assert.That(SmtSandboxDegree.MAX_DEGREE, Is.EqualTo(16));
            Assert.That(readme, Does.Contain($"Under the default options a session counts {Number(session / MIB)} MiB: a machine with 4 GiB available runs {words[Fit(4)]}, with 16 GiB {words[Fit(16)]}, with 64 GiB {words[Math.Min(Fit(64), SmtSandboxDegree.MAX_DEGREE)]} ({words[Fit(64)]} fit)."));
            Assert.That(readme, Does.Contain($"One session under the default options needs {Number(needs)} MiB available ({Number(session / MIB)} MiB at three quarters)"));
            Assert.That(readme, Does.Contain($"a margin of {SmtSandboxDegree.SESSION_MARGIN_BYTES / MIB} MiB for the host's part of a session"));
            Assert.That(readme, Does.Contain($"`[RequiresResources(MinRamMb = {SmtSandboxDegree.MIN_RAM_MB})]`"));
            Assert.That(readme, Does.Contain($"The degree is `min(logical cores, {SmtSandboxDegree.MAX_DEGREE}, sessions the memory holds)`"));
        });
    }

    [Test]
    public void TheLimitsTableIsTheCodesTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(CAPABILITIES), "## 7. Limits");
        var resultFrame = SmtPackageCodec.MAX_RESULT_FRAME_BYTES;

        var expected = new Dictionary<string, string[]>
        {
            ["Problems in a set"] = [$"{Number(SmtProblemSetLimits.MAX_ROWS)} rows, {SmtProblemSetLimits.MAX_SET_BYTES / GIB} GiB"],
            ["A problem"] = [$"{Number(SmtReaderLimits.PROBLEM_CHARACTERS)} characters, {Number(SmtReaderLimits.PROBLEM_TOKENS)} tokens, nesting {Number(SmtReaderLimits.PROBLEM_DEPTH)}"],
            ["A package"] =
            [
                $"{SmtPackagePlanner.MIN_PACKAGE_PROBLEMS} to {SmtPackageCodec.MAX_PACKAGE_PROBLEMS} problems, about {Number(SmtPackagePlanner.TARGET_PACKAGES)} packages",
                $"at most {SmtPackagePlanner.MAX_PACKAGE_CHARACTERS / MIB} Mi characters of text",
                $"a node takes at most {SmtPackageCodec.MAX_PACKAGE_PROBLEMS} problems and {SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES / MIB} MiB"
            ],
            ["A session's output"] = [$"{SmtProfile.OUTPUT_LIMIT_BYTES / MIB} MiB"],
            ["A row a node returns"] = [$"({SmtProfile.OUTPUT_LIMIT_BYTES / MIB} Mi characters together)", $"{resultFrame / MIB} MiB and {resultFrame % MIB / 1024} KiB"],
            ["What a lab sees of a row's models"] = [$"{Number(SmtResultsWriter.MAX_MODEL_BYTES)} bytes ({SmtResultsWriter.MAX_MODEL_BYTES / 1024} KiB) of UTF-8 a row"],
            ["A model the server reads"] =
            [
                $"{SmtHostEvaluator.MAX_MODEL_CHARACTERS / MIB} Mi characters, {Number(SmtHostEvaluator.MAX_MODEL_TOKENS)} tokens",
                $"{Number(SmtHostEvaluator.MAX_MODEL_NUMERAL_DIGITS)} digits of a number", $"a literal of {Number(SmtEvaluationBudget.DEFAULT_BIT_VECTOR_BITS)} bits",
                $"a string of {Number(SmtEvaluationBudget.DEFAULT_STRING_CHARACTERS)} characters", $"a name of {Number(SmtReaderLimits.PROBLEM_CHARACTERS)} characters"
            ],
            ["What the server's evaluator spends"] =
            [
                $"{Number(SmtEvaluationBudget.DEFAULT_STEPS)} steps a model", $"{Number(SmtPackageBudget.DEFAULT_STEPS)} steps the models of one result package",
                $"its check's setting up {Number(SmtPackageBudget.MODEL_STEPS)}"
            ],
            ["What the server keeps of one run while it settles it"] =
            [
                $"{SmtSettlementBounds.MAX_ROWS_BYTES / GIB} GiB of the nodes' rows, {SmtSettlementBounds.MAX_MODELS_BYTES / GIB} GiB of the full models",
                $"`{SmtSettlementBoundException.ROWS}` or `{SmtSettlementBoundException.MODELS}`"
            ],
            ["Memory of one session on a node"] = [$"and {SmtSandboxDegree.SESSION_MARGIN_BYTES / MIB} MiB: {Number(SmtSandboxDegree.SessionBytes(new SmtOptionsData()) / MIB)} MiB under the defaults"],
            ["Memory of a node's machine"] = [$"{Number(SmtSandboxDegree.MIN_RAM_MB)} MiB at least", "`node.min_ram_mb` of the capabilities", "three quarters of what it has available"],
            ["Sessions at once on a node"] = [$"`min(logical cores, {SmtSandboxDegree.MAX_DEGREE})`", $"at most {SmtSandboxDegree.MAX_DEGREE} packages open at once"],
            ["Duration"] = ["none is estimated; the upper bound is the taken problems times the hard cap"]
        };

        Assert.Multiple(() =>
        {
            Assert.That(table.Column(0), Is.EquivalentTo(expected.Keys), "every row of the limits is held here");
            Assert.That((SmtPackageBudget.STEPS_PER_CHARACTER, SmtSandboxDegree.RAM_HEADROOM), Is.EqualTo((4, 0.75)), "the words of the rows: four steps, three quarters");
            Assert.That(table.Row("What the server's evaluator spends")[1], Does.Contain("four steps a character"));
            foreach (var (row, parts) in expected)
            {
                foreach (var part in parts)
                    Assert.That(table.Row(row)[1], Does.Contain(part), row);
            }
        });
    }

    [Test]
    public void TheGlanceOfTheCapabilitiesIsTheCodesTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(CAPABILITIES), "## 2. At a glance");

        Assert.Multiple(() =>
        {
            Assert.That(table.Row("Solver")[1], Does.StartWith($"Z3 {SmtProfile.SOLVER_VERSION} for `wasm32-wasip1`").And.Contain($"profile `{SmtProfile.ID}`"));
            Assert.That(table.Row("Machines of the nodes")[1], Does.Contain($"({Number(SmtSandboxDegree.MIN_RAM_MB)} MiB as a node reports it"));
            Assert.That(table.Row("Budget of a problem")[1], Does.Contain($"({Number(SmtProfile.DEFAULT_RESOURCE_LIMIT)} by default)")
                .And.Contain($"({SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS} s of a reference machine by default, at {Number(SmtProfile.FUEL_PER_SECOND)} units a second)"));
            Assert.That(table.Row("Copies")[1], Does.Contain($"(the sampling rate, {SmtMarkdown.Plain(SmtOptionsRules.DEFAULT_SAMPLING_RATE)} by default)"));
        });
    }

    [Test]
    public void TheLimitsOfAJobAreTheCodesTest()
    {
        var page = SmtMarkdown.Read(APPLICATION);
        var limits = SmtMarkdown.Table(page, "### Limits of a job");
        var ends = SmtMarkdown.Table(page, "### How a job ends");

        Assert.Multiple(() =>
        {
            Assert.That(limits.Row("Rows of a set")[1], Does.StartWith($"{Number(SmtProblemSetLimits.MAX_ROWS)};").And.Contain($"(`{SmtRefusalCodes.TOO_MANY_PROBLEMS}`)"));
            Assert.That(limits.Row("Bytes of a set")[1], Does.StartWith($"{SmtProblemSetLimits.MAX_SET_BYTES / GIB} GiB ({Number(SmtProblemSetLimits.MAX_SET_BYTES)} bytes)").And.Contain($"(`{SmtRefusalCodes.SET_TOO_LARGE}`)"));
            Assert.That(limits.Row("A line of a set")[1], Does.StartWith($"{SmtProblemSetLimits.MAX_LINE_BYTES / MIB} MiB ({Number(SmtProblemSetLimits.MAX_LINE_BYTES)} bytes)").And.Contain($"(`{SmtRefusalCodes.ROW_TOO_LONG}`)"));
            Assert.That(limits.Row("A problem")[1], Does.StartWith($"{Number(SmtReaderLimits.PROBLEM_CHARACTERS)} characters, {Number(SmtReaderLimits.PROBLEM_TOKENS)} tokens, nesting {Number(SmtReaderLimits.PROBLEM_DEPTH)}"));
            Assert.That(limits.Row("What a lab sees of a row's models")[1], Does.StartWith($"{Number(SmtResultsWriter.MAX_MODEL_BYTES)} bytes of UTF-8 a row"));

            Assert.That(ends.Row("an option is out of range")[1], Does.StartWith($"`The run's options are refused: {Refusal(new SmtOptionsData { HardCapSeconds = -1 })}.`"));
            Assert.That(ends.Row("the profile is not the controller's")[1], Is.EqualTo($"`The run's options are refused: {ProfileSentence()}.`"));
            Assert.That(ends.Row("the nodes' rows, or the full models of the settled rows, pass what the server keeps of one run")[1],
                Is.EqualTo($"the bound's name: `{SmtSettlementBoundException.ROWS}` ({SmtSettlementBounds.MAX_ROWS_BYTES / GIB} GiB of rows) or `{SmtSettlementBoundException.MODELS}` ({SmtSettlementBounds.MAX_MODELS_BYTES / GIB} GiB of full models)"));
            Assert.That(Flat(page), Does.Contain($"a node that reports {Number(SmtSandboxDegree.MIN_RAM_MB)} MiB of memory or more"));
        });
    }

    #endregion

    #region Word Tests

    [Test]
    public void EveryCodeOfTheResultsIsExplainedOnceTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(APPLICATION), "### Every code");
        var listed = Capabilities().GetProperty("results").GetProperty("codes").EnumerateObject()
            .SelectMany(status => status.Value.EnumerateArray().Select(code => (status.Name, code.GetString()!))).ToList();
        var written = table.Rows.Select(row => (SmtMarkdown.Codes(row[0]).Single(), SmtMarkdown.Codes(row[1]).Single())).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EquivalentTo(listed), "a row for every code the capabilities list by status, and none beside them");
            Assert.That(table.Rows.Select(row => row[2]), Has.All.Not.Empty, "each code has its sentence");
            Assert.That(table.Cell("`refused`", "`index_too_large`"), Does.Contain($"above {Number(SmtSanitizer.MAX_INDEX)}"));
            Assert.That(table.Cell("`refused`", "`too_many_symbols`"), Does.Contain($"more than {Number(SmtSanitizer.MAX_DECLARED_SYMBOLS)} declared names"));
            Assert.That(table.Cell("`refused`", "`row_too_long`"), Does.Contain($"longer than {SmtProblemSetLimits.MAX_LINE_BYTES / MIB} MiB"));
            Assert.That(table.Cell("`refused`", "`id_invalid`"), Does.Contain($"1 to {SmtProblemSetLimits.MAX_ID_CHARACTERS} of the characters"));
            Assert.That(table.Cell("`refused`", "`too_many_problems`"), Does.Contain($"the row after the {Number(SmtProblemSetLimits.MAX_ROWS)}th"));
            Assert.That(table.Cell("`refused`", "`set_too_large`"), Does.Contain($"the line that passes {SmtProblemSetLimits.MAX_SET_BYTES / GIB} GiB"));
            Assert.That(table.Cell("`refused`", "`reserved_prefix`"), Does.Contain($"`{SmtSanitizer.RESERVED_PREFIX}`"));
        });
    }

    [Test]
    public void WhatTheUserCanDoNamesWordsTheDocumentsSayTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(APPLICATION), "### What the user can do");
        var capabilities = Capabilities();
        var words = Words(capabilities).Concat(Fields(SmtRunChecks.Schema("smt-results.schema.json"))).ToHashSet(StringComparer.Ordinal);
        var options = capabilities.GetProperty("defaults").EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var row in table.Rows)
            {
                Assert.That(SmtMarkdown.Codes(row[0]), Is.Not.Empty.And.SubsetOf(words), $"'{row[0]}' names words a document says");
                foreach (var option in SmtMarkdown.Codes(row[1]).Where(code => code.Contains('_') || code == "rlimit"))
                    Assert.That(words.Contains(option) || options.Contains(option) || option == nameof(SmtPreflightData.SessionMemoryBytes), Is.True, $"'{option}' is an option or a word of the documents");
            }
        });
    }

    [Test]
    public void TheDocumentsAreNamedAsTheirWritersNameThemTest()
    {
        var page = SmtMarkdown.Read(APPLICATION);
        var table = SmtMarkdown.Table(page, "### The documents");
        var flat = Flat(page);

        Assert.Multiple(() =>
        {
            Assert.That(table.Column(0), Is.EqualTo(new[] { "`results`", "`report`", "`integrity`", "`provenance`", "`ledger`" }));
            Assert.That(table.Row("`report`")[2], Does.EndWith($", `{SmtReportWriter.SCHEMA}`"));
            Assert.That(table.Row("`integrity`")[2], Does.EndWith($", `{SmtIntegrityWriter.SCHEMA}`"));
            Assert.That(table.Row("`provenance`")[2], Does.EndWith($", `{SmtProvenanceWriter.SCHEMA}`"));
            Assert.That(flat, Does.Contain($"`{SmtCapabilitiesWriter.SCHEMA}`, `{SmtReportWriter.SCHEMA}`, `{SmtIntegrityWriter.SCHEMA}` and `{SmtProvenanceWriter.SCHEMA}` in its `schema` field"));
            Assert.That(flat, Does.Contain($"A row's `digest` (`{SmtResultDigest.FORMAT}`)"));
            Assert.That(flat, Does.Contain($"`binding: {SmtRunWords.BINDINGS[1]}` (`{SmtRunWords.BINDINGS[2]}` when").And.Contain($"expects `{SmtRunWords.BINDINGS[0]}`"), "the bindings a page names");
        });
    }

    #endregion

    #region Preflight Tests

    [Test]
    public void ThePreflightTableIsItsTypeTest()
    {
        var page = SmtMarkdown.Read(APPLICATION);
        var table = SmtMarkdown.Table(page, "### The preflight");
        var flat = Flat(page);

        Assert.Multiple(() =>
        {
            Assert.That(table.Column(0).SelectMany(SmtMarkdown.Codes), Is.EquivalentTo(Members<SmtPreflightData>()), "a field of the table for every member, once");
            Assert.That(flat, Does.Contain($"the first {SmtPreflightData.MAX_REFUSALS} refusals in full"));
            foreach (var message in new[] { SmtPreflightReport.EMPTY, SmtPreflightReport.ALL_REFUSED, SmtPreflightReport.BOUND })
                Assert.That(page, Does.Contain($"- `{message}`"), "every fixed sentence of the messages");

            foreach (var member in Members<SmtRefusalData>())
                Assert.That(flat, Does.Contain($"`{member}`"), $"the refusal's {member}");
        });
    }

    [Test]
    public void TheDiagnosticsTableIsItsTypeTest()
    {
        var table = SmtMarkdown.Table(SmtMarkdown.Read(APPLICATION), "### The diagnostics");

        Assert.Multiple(() =>
        {
            Assert.That(table.Column(0).SelectMany(SmtMarkdown.Codes), Is.EquivalentTo(Members<SmtRuntimeDiagnosticsData>()), "a field of the table for every member, once");
            Assert.That(SmtMarkdown.Codes(table.Row("`TrapMode`")[1]), Is.EqualTo(new[] { SmtRuntimeDiagnosticsBuilder.TRAPS_EXPLICIT, SmtRuntimeDiagnosticsBuilder.TRAPS_SIGNALS }));
            Assert.That(SmtMarkdown.Codes(table.Row("`Platform`")[1]), Is.EqualTo(SmtCapabilitiesWriter.PLATFORMS));
            Assert.That(SmtMarkdown.Codes(table.Rows.Single(row => row[0].Contains("`DegreeReason`", StringComparison.Ordinal))[1]),
                Is.EqualTo(new[] { SmtSandboxDegree.BY_CORES, SmtSandboxDegree.BY_CAP, SmtSandboxDegree.BY_MEMORY, SmtSandboxDegree.NO_MEMORY }));
        });
    }

    #endregion

    #region Tools

    /// <summary>The capabilities document of this process's controller, without a pool.</summary>
    private static JsonElement Capabilities()
    {
        using var document = JsonDocument.Parse(SmtDiagnosticsRig.Capabilities(new SmtRuntimeDiagnosticsData { ModulePresent = true }));
        return document.RootElement.Clone();
    }

    /// <summary>Every word of every list of the capabilities document.</summary>
    private static IEnumerable<string> Words(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Words(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!),
            _ => []
        };
    }

    /// <summary>The names of the fields of a schema's top level.</summary>
    private static IEnumerable<string> Fields(JsonElement schema)
    {
        return schema.GetProperty("properties").EnumerateObject().Select(property => property.Name);
    }

    /// <summary>The members of a wire type of the model package, as its readers have them.</summary>
    private static List<string> Members<T>()
    {
        return [.. typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.GetCustomAttribute<MemoryPackOrderAttribute>() != null).Select(property => property.Name)];
    }

    /// <summary>The sentence an option out of range is refused with, the only one of the options.</summary>
    private static string Refusal(SmtOptionsData options)
    {
        return SmtOptionsRules.Check(options).Single();
    }

    /// <summary>The sentence a profile the controller does not have is refused with, as a preflight says it.</summary>
    private static string ProfileSentence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"witai_smt_docs_{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(path, "{\"id\": \"a\", \"smt2\": \"(declare-const x Int)\"}\n");
            return SmtRunRig.Preflight(path, new SmtOptionsData { Profile = "another" }).OptionProblems.Single();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A number as the documents write it.</summary>
    private static string Number(long number)
    {
        return SmtMarkdown.Number(number);
    }

    /// <summary>A whole power of ten as the README writes it: the digits, a middle dot, 10 and the exponent in superscript digits.</summary>
    private static string Power(long number)
    {
        var exponent = 0;
        while (number % 10 == 0)
        {
            number /= 10;
            exponent++;
        }

        char[] superscripts = [(char)0x2070, (char)0x00B9, (char)0x00B2, (char)0x00B3, (char)0x2074, (char)0x2075, (char)0x2076, (char)0x2077, (char)0x2078, (char)0x2079];
        var power = new string([.. exponent.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => superscripts[digit - '0'])]);
        return $"{number}{(char)0x00B7}10{power}";
    }

    /// <summary>The text of a document with its line breaks as blanks, so that a sentence reads whole.</summary>
    private static string Flat(string text)
    {
        return Regex.Replace(text, "\\s*\\n\\s*", " ");
    }

    /// <summary>The version a project file states.</summary>
    private static string ProjectVersion(string project)
    {
        var version = Regex.Match(SmtMarkdown.Read(project), "<Version>(?<v>[^<]+)</Version>");
        Assert.That(version.Success, Is.True, $"{project} states its version");
        return version.Groups["v"].Value;
    }

    /// <summary>A data asset of the controller's project: its archive, the release it is fetched from, its SHA-256 and its size.</summary>
    private static (string File, string Release, string Sha256, long Size) DataAsset(string name)
    {
        var item = Regex.Match(SmtMarkdown.Read(CONTROLLER_PROJECT), $"<ControllerDataAsset Include=\"{Regex.Escape(name)}\">(?<body>.*?)</ControllerDataAsset>", RegexOptions.Singleline).Groups["body"].Value;
        var uri = Regex.Match(item, "<Uri>gh-release://[^/]+/[^/]+/(?<release>[^/]+)/(?<file>[^<]+)</Uri>");
        return (uri.Groups["file"].Value, uri.Groups["release"].Value, Regex.Match(item, "<Sha256>(?<s>[^<]+)</Sha256>").Groups["s"].Value,
            long.Parse(Regex.Match(item, "<Size>(?<s>[^<]+)</Size>").Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Holds a version line of a document to the projects: the line the pattern finds, each of its groups the project's version.</summary>
    private static void AssertLine(string document, string pattern, params (string Group, string Version)[] versions)
    {
        var match = Regex.Match(SmtMarkdown.Read(document), pattern, RegexOptions.Multiline);
        Assert.That(match.Success, Is.True, $"{document}: the line that names the version ({pattern})");
        if (!match.Success)
            return;

        foreach (var (group, version) in versions)
            Assert.That(match.Groups[group].Value, Is.EqualTo(version), $"{document}: '{match.Value}' names another version than the project's; move the line with the project");
    }

    #endregion
}
