using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Docs;

/// <summary>
/// The documents state the limits the code holds (hardening batch B, item B4): every sentence
/// here is built from the code's constants and must stand in the document as it is written. A
/// limit that moves in the code without its sentence, in the README, the capabilities document
/// or a schema, fails here. The page for contributors has a test of its own
/// (<see cref="VerifySafetyPageTests"/>).
/// </summary>
[TestFixture]
public sealed class VerifyDocNumbersTests
{
    #region Constants

    private const string README = "README.md";
    private const string CAPABILITIES = "docs/capabilities.md";
    private const string TASKSET_SCHEMA = "docs/verify-taskset.schema.json";
    private const string CHECKER_SCHEMA = "docs/verify-checker.schema.json";
    private const string CAPABILITIES_SCHEMA = "docs/verify-capabilities.schema.json";
    private const string TASKSET_SECTION = "taskset";
    private const string CONTROLLER_PROJECT = "OutWit.Controller.AI.Verify.csproj";
    private const string MODEL_PROJECT = "../OutWit.Controller.AI.Verify.Model/OutWit.Controller.AI.Verify.Model.csproj";
    private const string SCRIPTS_PROJECT = "../OutWit.Controller.AI.Verify.Scripts/OutWit.Controller.AI.Verify.Scripts.csproj";

    private const long KIBIBYTE = 1024;
    private const long MEBIBYTE = 1024 * KIBIBYTE;
    private const long GIBIBYTE = 1024 * MEBIBYTE;
    private const long THOUSAND_MILLION = 1_000_000_000;

    #endregion

    #region Tests

    [Test]
    public void B4TheDocumentsStateTheLimitsTheCodeHoldsTest()
    {
        // One test for all the sentences, and no test case source: a source runs when the tests
        // are discovered, and nothing of the controller may be loaded then (VerifyModuleLoadTests).
        var documents = new Dictionary<string, string>();
        var missing = new List<string>();

        foreach (var (document, sentence) in Sentences())
        {
            if (!documents.TryGetValue(document, out var text))
                documents[document] = text = Flat(Load(document));

            if (!text.Contains(sentence, StringComparison.Ordinal))
                missing.Add($"{document}: {sentence}");
        }

        Assert.That(VerifyPackageTransfer.TRANSFER_BASE_TIME, Is.EqualTo(TimeSpan.FromMinutes(2)), "the documents write this number as a word: two minutes");
        Assert.That(missing, Is.Empty, "a document no longer states what the code holds:\n" + string.Join("\n", missing));
    }

    [Test]
    public void B4TheDocumentsStateTheBoundsTheCapabilitiesDocumentCarriesTest()
    {
        // The bounds of a taskset and of a preflight's answer are in three places: the code's
        // constants, the capabilities document a deployment returns, and the pages that describe
        // it. The document is held to the constants, and the pages to the document: every field
        // of its section with the number it carries, and the version of the document itself.
        var bounds = new (string Field, long Value)[]
        {
            ("max_line_bytes", VerifyTasksetLimits.MAX_LINE_BYTES),
            ("max_tasks", VerifyTasksetLimits.MAX_TASKS),
            ("max_sources", VerifyTasksetLimits.MAX_SOURCES),
            ("max_args", VerifyTasksetLimits.MAX_ARGS),
            ("max_suite_cases", VerifyTasksetLimits.MAX_SUITE_CASES),
            ("max_preflight_messages", VerifyPreflightReport.MaxMessages),
            ("max_preflight_messages_per_kind", VerifyTasksetLimits.MAX_MESSAGES),
            ("max_preflight_message_length", VerifyTasksetLimits.MAX_MESSAGE_LENGTH),
            ("max_preflight_unknown_runtimes", VerifyTasksetLimits.MAX_LISTED_RUNTIMES)
        };

        using var written = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(new VerifyRuntimeDiagnosticsData(), null));
        var carried = written.RootElement.GetProperty(TASKSET_SECTION).EnumerateObject().Select(field => (field.Name, field.Value.GetInt64())).ToList();
        var capabilities = Flat(Load(CAPABILITIES));
        var readme = Flat(Load(README));
        var schema = Flat(Load(CAPABILITIES_SCHEMA));

        Assert.Multiple(() =>
        {
            Assert.That(carried, Is.EqualTo(bounds), "the capabilities document carries the code's bounds");

            foreach (var (field, value) in carried)
            {
                Assert.That(capabilities, Does.Contain($"| `{field}` | {Count(value)} |"), $"{CAPABILITIES} states {TASKSET_SECTION}.{field} with the number the document carries");
                Assert.That(readme, Does.Contain($"`{field}`"), $"{README} names {TASKSET_SECTION}.{field}");
                Assert.That(schema, Does.Contain($"\"{field}\": {{ \"type\": \"integer\""), $"{CAPABILITIES_SCHEMA} has {TASKSET_SECTION}.{field}");
            }

            Assert.That(capabilities, Does.Contain($"| `max_line_bytes` | {Count(VerifyTasksetLimits.MAX_LINE_BYTES)} | the bytes of one line of a taskset ({Size(VerifyTasksetLimits.MAX_LINE_BYTES)})"));
            Assert.That(capabilities, Does.Contain($"[`verify-capabilities.schema.json`](verify-capabilities.schema.json), `{VerifyCapabilitiesWriter.SCHEMA}`)"));
            Assert.That(capabilities, Does.Contain($"The `{TASKSET_SECTION}` section is new in `{VerifyCapabilitiesWriter.SCHEMA}`."));
            Assert.That(capabilities, Does.Contain($"**A new document version.** `{VerifyCapabilitiesWriter.SCHEMA}`"));
            Assert.That(readme, Does.Contain($"| capabilities (`{VerifyCapabilitiesWriter.SCHEMA}`) |"));
            Assert.That(readme, Does.Contain($"in a section of their own (`{TASKSET_SECTION}`), and is `{VerifyCapabilitiesWriter.SCHEMA}` for it"));
        });
    }

    [Test]
    public void B4TheDocumentsNameTheVersionsOfTheProjectsTest()
    {
        var controller = Version(CONTROLLER_PROJECT);
        var model = Version(MODEL_PROJECT);
        var scripts = Version(SCRIPTS_PROJECT);

        Assert.Multiple(() =>
        {
            Assert.That(controller, Is.EqualTo(ControllerBuildInfo.VERSION), "the controller's project and its build agree");
            Assert.That(Flat(Load(README)), Does.Contain($"**Status: {controller} (Verify.Model {model}, Verify.Scripts {scripts})"));
            Assert.That(Flat(Load(CAPABILITIES)), Does.Contain($"Version described: **Verify {controller}** (with Verify.Model {model} and Verify.Scripts {scripts})"));
            Assert.That(Flat(Load(CAPABILITIES)), Does.Contain($"| The controller | {controller} |"));
            Assert.That(Flat(Load(CAPABILITIES)), Does.Contain($"| The model and the scripts | {model} and {scripts} |"));
        });
    }

    [Test]
    public void M9TheDocumentsSayWhatIsReleasedAndWhatIsComparedTest()
    {
        // Audit finding V:M-9: the documents said that licences travel inside the archives
        // (with a list that was not complete) and "byte for byte" of archives that were packed
        // once by hand. They now name the releases the project file fetches from, the notices
        // file of every folder, and the job that builds each archive again and compares it.
        var releases = Regex.Matches(Load(CONTROLLER_PROJECT), "<Uri>gh-release://[^/]+/[^/]+/(?<tag>[^/]+)/[^<]+</Uri>")
            .Select(match => match.Groups["tag"].Value)
            .ToList();
        var readme = Flat(Load(README));
        var capabilities = Flat(Load(CAPABILITIES));

        Assert.Multiple(() =>
        {
            Assert.That(releases, Has.Count.EqualTo(5), "the native library of three platforms, the language runtimes, the numpy runtime");

            foreach (var release in releases.Distinct())
                Assert.That(capabilities, Does.Contain($"release `{release}`"), "what a deployment carries is named by the release the project file fetches it from");

            foreach (var (name, document) in new[] { (README, readme), (CAPABILITIES, capabilities) })
            {
                Assert.That(document, Does.Not.Contain("not yet complete"), $"{name}: the notices are complete, or the sentence says where they end");
                Assert.That(document, Does.Not.Contain("packed once"), $"{name}: an archive is built again and compared");
                Assert.That(document, Does.Contain("`THIRD-PARTY-NOTICES.txt`"), name);
                Assert.That(document, Does.Contain("not by asking"), $"{name}: how the list of the engine's crates was made, and so where it ends");
            }

            Assert.That(readme, Does.Contain("the `AI Verify archives` workflow builds them again at every change of the asset tool, of a notices file or of a pin, and fails when one is not the archive the controller's project file pins by SHA-256 and size"));
            Assert.That(capabilities, Does.Contain("a job builds each again from its pins at every change and fails when it is not the archive the controller's project file pins by SHA-256 and size"));
        });
    }

    [Test]
    public void B4TheScriptsInTheReadmeAreTheBundledScriptsTest()
    {
        // The README shows the scripts "as they are": each is held to its file.
        var readme = Load(README).Replace("\r\n", "\n");

        Assert.Multiple(() =>
        {
            foreach (var script in new[] { "VerifyRun.wit", "VerifyGenerate.wit", "VerifyGenerateAndCheck.wit", "VerifyPreflight.wit", "VerifyRuntimeDiagnostics.wit" })
                Assert.That(readme, Does.Contain(Load(Path.Combine("Scripts", script)).Replace("\r\n", "\n").Trim()), script);
        });
    }

    #endregion

    #region Tools

    private static List<(string Document, string Sentence)> Sentences()
    {
        var defaultFuel = VerifySandboxDefaults.DEFAULT_FUEL_BUDGET / THOUSAND_MILLION;
        var maxFuel = VerifyLimitCeilings.MAX_FUEL_BUDGET / THOUSAND_MILLION;
        var defaultMemory = Size(VerifySandboxDefaults.DEFAULT_MEMORY_BYTES);
        var maxMemory = Size(VerifyLimitCeilings.MAX_MEMORY_BYTES);
        var defaultStdout = Size(VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES);
        var maxStdout = Size(VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES);
        var defaultStderr = Size(VerifySandboxDefaults.DEFAULT_STDERR_LIMIT_BYTES);
        var maxStderr = Size(VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES);
        var backstopDefault = VerifyBackstop.WallMs(VerifySandboxDefaults.DEFAULT_FUEL_BUDGET) / 1000;
        var backstopMax = VerifyBackstop.WallMs(VerifyLimitCeilings.MAX_FUEL_BUDGET) / 1000;
        var taskOutput = VerifyPackagePlanner.OutputBytes(VerifySandboxDefaults.Resolve(null, null));
        var tasksInBatch = VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES / taskOutput;
        var tasksInPackage = VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES / taskOutput;
        var cases = Count(VerifyTasksetLimits.MAX_SUITE_CASES);
        var tasks = Count(VerifyTasksetLimits.MAX_TASKS);
        var line = Size(VerifyTasksetLimits.MAX_LINE_BYTES);
        var text = Size(VerifyPackagePlanner.MAX_PACKAGE_BYTES);

        return
        [
            // The limits of a run.
            (CAPABILITIES, $"| Fuel | {defaultFuel}e9 | {maxFuel}e9 | `Timeout` |"),
            (CAPABILITIES, $"| Memory (one linear memory, 32-bit addresses) | {defaultMemory} | {maxMemory} |"),
            (CAPABILITIES, $"| stdout, of a task with all its cases | {defaultStdout} | {maxStdout} |"),
            (CAPABILITIES, $"| stderr | {defaultStderr} | {maxStderr} |"),
            (CAPABILITIES, $"fuel (one unit per instruction, {Count(WasmHostMeter.CALL_FUEL)} a call into the host and one a byte the call moves) {defaultFuel}e9 by default and {maxFuel}e9 at most; memory {defaultMemory} / {maxMemory}; stdout {defaultStdout} / {maxStdout}"),
            (CAPABILITIES, $"**What costs fuel:** one unit an instruction; {Count(WasmHostMeter.CALL_FUEL)} units a call into the host, whoever answers it; and one unit a byte the call moves"),
            (CAPABILITIES, $"({WasmHostLinker.SUBSCRIPTION_SIZE} a subscription, {WasmHostLinker.EVENT_SIZE} an event)"),
            (README, $"fuel, one unit an instruction, {Count(WasmHostMeter.CALL_FUEL)} a call into the host and one a byte the call moves"),
            (README, $"a call into the host also costs a fixed {Count(WasmHostMeter.CALL_FUEL)} units"),
            (README, $"{defaultFuel}e9 by default (about ten seconds of a plain busy loop on the slowest node the fleet has shown, at {Count(VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND / 1_000_000)} million units a second), {maxFuel}e9 at most"),
            (README, $"one linear memory of 32-bit addresses: {defaultMemory} by default, {maxMemory} at most"),
            (README, $"stdout: {defaultStdout} by default, {maxStdout} at most, for a task with all the cases of its suite. stderr: {defaultStderr} by default, {maxStderr} at most."),
            (TASKSET_SCHEMA, $"every field is clamped to the ceilings (fuel {maxFuel}e9, memory {maxMemory}, stdout {maxStdout}, stderr {maxStderr})"),
            (TASKSET_SCHEMA, $"Default {defaultFuel}e9, about ten seconds of a busy loop on a slow machine."),
            (TASKSET_SCHEMA, $"Linear memory cap in bytes; default {defaultMemory}."),
            (TASKSET_SCHEMA, $"all cases of a suite together; default {defaultStdout}."),
            (TASKSET_SCHEMA, $"Captured stderr cap; default {defaultStderr}."),

            // Fuel and time.
            (CAPABILITIES, $"**The reference rate** is {Count(VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND / 1_000_000)} million units a second"),
            (CAPABILITIES, $"{VerifyBackstop.BASE_MS / 1000} seconds plus the time the budget lasts at {VerifyBackstop.SLOWEST_FUEL_PER_SECOND / 1_000_000} million units a second"),
            (CAPABILITIES, $"That is {backstopDefault} s for the default budget and {backstopMax} s for the largest."),
            (README, $"{VerifyBackstop.BASE_MS / 1000} s plus the time the fuel budget lasts at {VerifyBackstop.SLOWEST_FUEL_PER_SECOND / 1_000_000} million units a second: {backstopDefault} s for the default budget, {backstopMax} s for the largest"),

            // A module a task brings.
            (CAPABILITIES, $"| A module a task brings (`wasi`) | {Size(VerifyWasmSandbox.MAX_TASK_MODULE_BYTES)} | | `CompileError` |"),
            (CAPABILITIES, $"| Blocks nested in one function of such a module, or of a built C/C++ program | {Count(VerifyModuleShape.MAX_NESTING)} | | `CompileError` |"),
            (CAPABILITIES, $"| The product of a C/C++ build step | {Size(VerifyClangBuild.PRODUCT_LIMIT_BYTES)} | | `CompileError` |"),
            (CAPABILITIES, $"The price is {Count(VerifyModuleShape.COMPILE_FUEL_PER_BYTE)} units a byte of the module"),
            (README, $"{Count(VerifyModuleShape.COMPILE_FUEL_PER_BYTE)} units of fuel a byte of the module"),
            (README, $"Blocks nested deeper than {Count(VerifyModuleShape.MAX_NESTING)} in one function are refused"),
            (README, $"A raw module may be at most {Size(VerifyWasmSandbox.MAX_TASK_MODULE_BYTES)}."),

            // The bounds of a task's module, and what its shape costs.
            (CAPABILITIES, $"| Parameters, and results, of a function type of such a module or program | {Count(VerifyModuleShape.MAX_PARAMETERS)}, and {Count(VerifyModuleShape.MAX_RESULTS)} | | `CompileError` |"),
            (CAPABILITIES, $"| Values a block of such a module or program takes and gives | {Count(VerifyModuleShape.MAX_BLOCK_VALUES)} | | `CompileError` |"),
            (CAPABILITIES, $"| Locals of one function, and declared by all the functions of such a module or program | {Count(VerifyModuleShape.MAX_LOCALS)}, and {Count(VerifyModuleShape.MAX_MODULE_LOCALS)} | | `CompileError` |"),
            (CAPABILITIES, $"| Functions of such a module or program | {Count(VerifyModuleShape.MAX_FUNCTIONS)} | | `CompileError` |"),
            (CAPABILITIES, $"| The body of one function of such a module or program | {Size(VerifyModuleShape.MAX_BODY_BYTES)} | | `CompileError` |"),
            (CAPABILITIES, $"| Everything a C/C++ build writes, its objects and its program | {Size(VerifyClangBuild.BUILD_LIMIT_BYTES)} | | `CompileError` |"),
            (CAPABILITIES, $"{VerifyModuleShape.COMPILE_FUEL_PER_BRANCH_UNIT} units for every unit of (the branches of a function)², for a function of more than {Count(VerifyModuleShape.FREE_BRANCHES)} branches; {VerifyModuleShape.COMPILE_FUEL_PER_LIVE_UNIT} units for every unit of (the locals a function uses) × (its branches) past {Count(VerifyModuleShape.FREE_LIVE_UNITS)}; {VerifyModuleShape.COMPILE_FUEL_PER_BLOCK_UNIT} units for every unit of (the values a block passes) × (the blocks around it), for a block of two values or more; {VerifyModuleShape.COMPILE_FUEL_PER_LOCAL} units a local past {VerifyModuleShape.FREE_LOCALS} in a function; and a function is priced as {VerifyModuleShape.FUNCTION_FLOOR_BYTES} bytes at the least"),
            (CAPABILITIES, $"`a function type of the module has more than {VerifyModuleShape.MAX_PARAMETERS} parameters`, `... more than {VerifyModuleShape.MAX_RESULTS} results`, `a block of the module takes and gives more than {VerifyModuleShape.MAX_BLOCK_VALUES} values`, `a function of the module has more than {VerifyModuleShape.MAX_LOCALS} locals`, `the functions of the module declare more than {VerifyModuleShape.MAX_MODULE_LOCALS} locals together`, `the module has more than {VerifyModuleShape.MAX_FUNCTIONS} functions`, `a function of the module is larger than {VerifyModuleShape.MAX_BODY_BYTES >> 10} KiB`, and `{VerifyModuleShape.START_FUNCTION}`"),
            (CAPABILITIES, $"Compiles that may take {Size(VerifyWasmSandbox.HEAVY_COMPILE_BYTES)} or more share {Count(VerifyWasmSandbox.CompileGateBytes / MEBIBYTE)} MiB between"),
            (CAPABILITIES, $"A module estimated over {Count(VerifyCompileMemory.MAX_BYTES / MEBIBYTE)} MiB is"),
            (CAPABILITIES, "Two kinds of code compile far more slowly than their price says: calls through a table"),
            (CAPABILITIES, "a module of many such functions can hold one core of a node for minutes while it compiles"),
            (README, "Two kinds of code compile far more slowly than their price says: calls through a table, and loads and stores on a host with hardware stack protection"),
            (CAPABILITIES, $"`{VerifyModuleShape.TOO_MUCH_MEMORY}`"),
            (CAPABILITIES, $"{Size(VerifyCompileMemory.PER_FUNCTION)} a function and {Size(VerifyCompileMemory.PER_BODY_BYTE)} a byte of the function the compiler works on"),
            (CAPABILITIES, $"{VerifyCompileMemory.PER_KEPT_UNIT} bytes a unit, an instruction of arithmetic {VerifyCompileMemory.UNITS_PLAIN} units, a branch {VerifyCompileMemory.UNITS_BRANCH}, a call {VerifyCompileMemory.UNITS_CALL} and one a value it passes, a load or a store {VerifyCompileMemory.UNITS_MEMORY}, a division {VerifyCompileMemory.UNITS_DIVISION}, a call through a table {VerifyCompileMemory.UNITS_INDIRECT_CALL} and one a value, a conversion that may trap {VerifyCompileMemory.UNITS_TRUNCATION}"),
            (README, $"a function type has at most {Count(VerifyModuleShape.MAX_PARAMETERS)} parameters and {Count(VerifyModuleShape.MAX_RESULTS)} results, a block passes at most {Count(VerifyModuleShape.MAX_BLOCK_VALUES)} values, a function has at most {Count(VerifyModuleShape.MAX_LOCALS)} locals and a body of {Size(VerifyModuleShape.MAX_BODY_BYTES)}, a module at most {Count(VerifyModuleShape.MAX_FUNCTIONS)} functions and {Count(VerifyModuleShape.MAX_MODULE_LOCALS)} declared locals"),
            (README, $"when a function type has more than {Count(VerifyModuleShape.MAX_PARAMETERS)} parameters or more than {Count(VerifyModuleShape.MAX_RESULTS)} results, a block takes and gives more than {Count(VerifyModuleShape.MAX_BLOCK_VALUES)} values, a function has more than {Count(VerifyModuleShape.MAX_LOCALS)} locals or a body larger than {Size(VerifyModuleShape.MAX_BODY_BYTES)}, or the module has more than {Count(VerifyModuleShape.MAX_FUNCTIONS)} functions or its functions declare more than {Count(VerifyModuleShape.MAX_MODULE_LOCALS)} locals together"),
            (README, $"{VerifyModuleShape.COMPILE_FUEL_PER_BRANCH_UNIT} units for every unit of (branches of a function)², for a function of more than {Count(VerifyModuleShape.FREE_BRANCHES)} branches; {VerifyModuleShape.COMPILE_FUEL_PER_LIVE_UNIT} units for every unit of (locals a function uses) × (its branches) past {Count(VerifyModuleShape.FREE_LIVE_UNITS)}; {VerifyModuleShape.COMPILE_FUEL_PER_BLOCK_UNIT} units for every unit of (values a block passes) × (blocks around it), for a block of two values or more; {VerifyModuleShape.COMPILE_FUEL_PER_LOCAL} units a local past {VerifyModuleShape.FREE_LOCALS} in a function; and a function is priced as {VerifyModuleShape.FUNCTION_FLOOR_BYTES} bytes at the least"),
            (README, $"is capped at {Size(VerifyClangBuild.BUILD_LIMIT_BYTES)}, at the write as well"),
            (README, $"or more share {Count(VerifyWasmSandbox.CompileGateBytes / MEBIBYTE)} MiB between them in a node's process"),
            (README, $"a module estimated over {Count(VerifyCompileMemory.MAX_BYTES / MEBIBYTE)} MiB is refused"),
            (README, $"(`{VerifyModuleShape.TOO_MUCH_MEMORY}`)"),
            (README, $"{Size(VerifyCompileMemory.PER_BODY_BYTE)} a byte of the function it works on"),
            (README, $"the {Count(VerifyWasmSandbox.CompileGateBytes / MEBIBYTE)} MiB the larger compiles share"),
            (README, $"the {Size(VerifyTaskModuleCache.DEFAULT_BYTE_CAPACITY)} of compiled code the node keeps for the tasks to come"),

            // What a taskset may hold.
            (CAPABILITIES, $"| Cases of a suite | | {cases} | the taskset is refused |"),
            (CAPABILITIES, $"| Sources of a task | | {Count(VerifyTasksetLimits.MAX_SOURCES)} | the taskset is refused |"),
            (CAPABILITIES, $"| Arguments of a task, and of a case | | {Count(VerifyTasksetLimits.MAX_ARGS)} | the taskset is refused |"),
            (CAPABILITIES, $"| Text of a task (sources and their names, arguments, inputs, expected outputs) | | {text} (characters) | the taskset is refused |"),
            (CAPABILITIES, $"| A line of a taskset | | {line} | the taskset is refused |"),
            (CAPABILITIES, $"| Tasks of a taskset | | {tasks} | the taskset is refused |"),
            (CAPABILITIES, $"A line of {line}, {Count(VerifyTasksetLimits.MAX_SOURCES)} sources, {Count(VerifyTasksetLimits.MAX_ARGS)} arguments and {cases} cases a task, {tasks} tasks a taskset"),
            (CAPABILITIES, $"up to {cases} runs of up to {maxFuel}e9 fuel each"),
            (CAPABILITIES, $"at most {Count(VerifySourceNames.MAX_NAME_BYTES)} bytes in all and {VerifySourceNames.MAX_DEPTH} segments"),
            (README, $"a line ({line}), the sources, arguments and cases of a task ({cases} each), the tasks of a taskset ({tasks})"),
            (README, $"more than {cases} sources, arguments or cases; a line over {line}. A taskset has at most {tasks} tasks."),
            (README, $"a suite has at most {cases} cases"),
            (README, $"at most {Count(VerifySourceNames.MAX_NAME_BYTES)} bytes in all and {VerifySourceNames.MAX_DEPTH} segments"),
            (TASKSET_SCHEMA, $"a line is at most {VerifyTasksetLimits.MAX_LINE_BYTES} bytes ({line})"),
            (TASKSET_SCHEMA, $"is at most {VerifyPackagePlanner.MAX_PACKAGE_BYTES} characters ({text})"),
            (TASKSET_SCHEMA, $"a taskset has at most {VerifyTasksetLimits.MAX_TASKS} tasks"),
            (TASKSET_SCHEMA, $"File name → file content, at most {VerifyTasksetLimits.MAX_SOURCES} files."),
            (TASKSET_SCHEMA, $"Test cases, at most {VerifyTasksetLimits.MAX_SUITE_CASES}."),
            (CHECKER_SCHEMA, $"UTF-8, at most {VerifyTasksetLimits.MAX_LINE_BYTES} bytes."),

            // What a preflight answers.
            (CAPABILITIES, $"at most {VerifyTasksetLimits.MAX_MESSAGES} messages of each kind (line errors, clamped limits, tasks that cannot be planned), each at most {VerifyTasksetLimits.MAX_MESSAGE_LENGTH} characters"),
            (CAPABILITIES, $"A preflight never returns more than {VerifyPreflightReport.MaxMessages} messages."),
            (CAPABILITIES, $"at most {VerifyTasksetLimits.MAX_LISTED_RUNTIMES} unknown ids are listed"),
            (CAPABILITIES, $"At most {VerifyTasksetLimits.MAX_MESSAGES} messages of a kind, of {VerifyTasksetLimits.MAX_MESSAGE_LENGTH} characters each"),
            (README, $"what a preflight answers ({VerifyTasksetLimits.MAX_MESSAGES} messages of a kind, {VerifyTasksetLimits.MAX_MESSAGE_LENGTH} characters each)"),

            // How a run is cut.
            (CAPABILITIES, $"| `BatchSize` | {VerifyTasksetPlanner.DEFAULT_BATCH_SIZE} (0 means the default) | tasks per batch, clamped to {Count(VerifyBatchExecutor.MAX_BATCH_TASKS)}"),
            (CAPABILITIES, $"| `MaxDegree` | {VerifySandboxDegree.DEFAULT_MAX_DEGREE} (0 means the default) |"),
            (CAPABILITIES, $"A batch holds {VerifyTasksetPlanner.DEFAULT_BATCH_SIZE} tasks by default, {Count(VerifyBatchExecutor.MAX_BATCH_TASKS)} at most. It is also cut where its tasks' text reaches {text} (characters) or their output caps together reach {Size(VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES)}."),
            (CAPABILITIES, $"A package holds at most {Count(VerifyPackagePlanner.MAX_PACKAGE_TASKS)} tasks, {text} of task text and {Size(VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES)} of output caps."),
            (CAPABILITIES, $"At the default caps a task counts for {Size(taskOutput)}, so a batch holds at most {tasksInBatch} such tasks and a package {tasksInPackage}"),
            (README, $"`BatchSize` (tasks per batch, 0 = {VerifyTasksetPlanner.DEFAULT_BATCH_SIZE}, clamped to {Count(VerifyBatchExecutor.MAX_BATCH_TASKS)}), `MaxDegree` (concurrent sandboxes per node, 0 = {VerifySandboxDegree.DEFAULT_MAX_DEGREE})"),
            (README, $"A batch holds {VerifyTasksetPlanner.DEFAULT_BATCH_SIZE} tasks by default, {Count(VerifyBatchExecutor.MAX_BATCH_TASKS)} at most. It is also cut where its tasks' text reaches {text} (characters) or their output caps together reach {Size(VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES)}"),
            (README, $"A package holds at most {Count(VerifyPackagePlanner.MAX_PACKAGE_TASKS)} tasks, {text} of task text and {Size(VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES)} of output caps."),
            (README, $"At the default caps a task counts for {Size(taskOutput)}, so a batch holds at most {tasksInBatch} such tasks and a package {tasksInPackage}"),

            // Transfers.
            (CAPABILITIES, $"One attempt of a blob transfer has a time to finish in: two minutes plus the time its bytes take at {Size(VerifyPackageTransfer.TRANSFER_SLOWEST_BYTES_PER_SECOND)} a second."),
            (README, $"Every attempt of a transfer has a time to finish in: two minutes plus the time its bytes take at {Size(VerifyPackageTransfer.TRANSFER_SLOWEST_BYTES_PER_SECOND)} a second.")
        ];
    }

    /// <summary>A size as the documents write one: 8 MiB, 256 KiB, 2 GiB.</summary>
    private static string Size(long bytes)
    {
        if (bytes % GIBIBYTE == 0)
            return $"{bytes / GIBIBYTE} GiB";

        return bytes % MEBIBYTE == 0 ? $"{bytes / MEBIBYTE} MiB" : $"{bytes / KIBIBYTE} KiB";
    }

    /// <summary>A count as the documents write one: 4,096.</summary>
    private static string Count(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>A document with its line breaks and the indentation after them taken out: a sentence stands in it however it is wrapped.</summary>
    private static string Flat(string text)
    {
        return Regex.Replace(text, @"\s*\r?\n\s*", " ");
    }

    private static string Version(string project)
    {
        var match = Regex.Match(Load(project), "<Version>(?<version>[0-9.]+)</Version>");
        Assert.That(match.Success, Is.True, $"{project} states its version");
        return match.Groups["version"].Value;
    }

    private static string Load(string relativePath)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;
        if (directory == null)
            Mock.VerifyRigModules.Missing("Solution root not found");

        var path = Path.GetFullPath(Path.Combine(directory!.FullName, "AI", "OutWit.Controller.AI.Verify", relativePath));
        Assert.That(File.Exists(path), Is.True, $"missing: {path}");
        return File.ReadAllText(path);
    }

    #endregion
}
