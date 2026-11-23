using System.Collections;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Docs;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Node;

/// <summary>
/// Packages of problems through the engine to in-process nodes, as a run sends them: references
/// go out, blobs are fetched, the problems are solved, references to rows come back. The golden
/// problems come back with their rows, whose digests are the golden file's; a package solved on two
/// nodes gives equal digests; a package a node does not take comes back with its code; a package
/// larger than one chunk of the blob channel goes and comes; and what the host's module says of
/// itself. Nothing that comes back names a node.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtEngineTests : SmtEngineTestBase
{
    #region Constants

    private const string SOLVE = """
        Job:SmtSolveShare(SmtTaskPackageCollection:packages)
        {
            SmtResultPackageCollection:results = Grid.ForEach(package in packages)
                => Smt.SolvePackage(package);
        }
        """;

    private const string SOLVE_TWICE = """
        Job:SmtSolveTwice(SmtTaskPackageCollection:packages)
        {
            ProcessingOptions:twice = Grid.Replicas(2);
            SmtResultPackageCollection:results = Grid.ForEach(package in packages, twice)
                => Smt.SolvePackage(package);
        }
        """;

    private const string DIAGNOSTICS = """
        Job:SmtDiagnostics()
        {
            SmtRuntimeDiagnostics:result = Smt.RuntimeDiagnostics();
            Blob:capabilities = Smt.Capabilities();
        }
        """;

    /// <summary>The chunk the blob channel moves a blob in: a package larger than this is more than one chunk.</summary>
    private const long BLOB_CHUNK_BYTES = 10L * 1024 * 1024;

    #endregion

    #region Golden Tests

    [Test]
    public async Task TheGoldenPackagesGoOutAndComeBackWithTheirRowsTest()
    {
        var packages = await PublishGoldenAsync();

        var results = await SolveAsync(SOLVE, packages);
        var rows = await RowsAsync(packages, results);

        var golden = SmtRowGolden.Read();
        Assert.That(golden, Has.Count.EqualTo(SmtSolverCases.All.Count), $"the golden file {SmtRowGolden.FILE}");

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(packages.Count), "a result for every package");
            Assert.That(results.Select(result => result.FaultCode), Has.All.Empty);
            Assert.That(rows.Select(row => row.Index).Order(), Is.EqualTo(Enumerable.Range(0, SmtSolverCases.All.Count)), "a row for every problem, once");

            foreach (var (id, index, digest) in golden)
            {
                var row = rows.Single(one => one.Index == index);
                Assert.That(row.Id, Is.EqualTo(id));
                Assert.That(row.Digest, Is.EqualTo(digest), $"{id}: the digest of the golden file");
                Assert.That(SmtResultDigest.Holds(row), Is.True, id);
                Assert.That(SmtNodeRig.Words(row), Is.EqualTo(SmtSolverCases.Of(id).Expected), id);
                Assert.That(row.Platform, Is.EqualTo(SmtNodeRig.Platform));
                Assert.That(row.ControllerVersion, Is.EqualTo(SmtNodeRig.ControllerVersion));
            }
        });

        AssertNamesNoMachine(rows, results);
    }

    [Test]
    public async Task ThePackagesSolvedOnTwoNodesGiveEqualDigestsTest()
    {
        var packages = await PublishGoldenAsync();

        var results = await SolveAsync(SOLVE_TWICE, packages);
        var rows = await RowsAsync(packages, results);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(2 * packages.Count), "every package twice");
            Assert.That(rows, Has.Count.EqualTo(2 * SmtSolverCases.All.Count));

            foreach (var copies in rows.GroupBy(row => row.Index))
            {
                var pair = copies.ToList();
                Assert.That(pair, Has.Count.EqualTo(2), $"problem {copies.Key}: two copies");
                Assert.That(pair[1].Digest, Is.EqualTo(pair[0].Digest), $"{pair[0].Id}: two machines, one digest");
                Assert.That(pair[1].Ticket, Is.EqualTo(pair[0].Ticket), $"{pair[0].Id}: copies of one package carry its ticket");
            }
        });
    }

    #endregion

    #region Refusal Tests

    [Test]
    public async Task APackageANodeDoesNotTakeComesBackWithItsCodeTest()
    {
        var good = await PublishAsync([SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION)]);

        // Another controller version.
        var version = await PublishAsync([SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION)]);
        version.RequiredControllerVersion = "0.0.1";

        // A blob damaged after it was stored, its reference true to the damaged bytes.
        var corrupted = await PublishAsync([SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION)]);
        var bytes = await File.ReadAllBytesAsync(await Blobs.GetLocalPathAsync(corrupted.ProblemsBlobId));
        for (var offset = 12; offset < bytes.Length; offset += 3)
            bytes[offset] ^= 0x33;
        Blobs.Overwrite(corrupted.ProblemsBlobId, bytes);
        corrupted.Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

        // A reference with another SHA-256 than its blob's.
        var hashed = await PublishAsync([SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION)]);
        hashed.Sha256 = new string('0', 64);

        // A ticket that is none.
        var ticketless = await PublishAsync([SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION)]);
        ticketless.Ticket = "not a ticket";

        // A blob larger than a node takes.
        var oversized = await PublishAsync([SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION)]);
        oversized.SizeBytes = SmtNodeRig.MaxPackageBytes + 1;

        var packages = new List<SmtTaskPackageData> { good, version, corrupted, hashed, ticketless, oversized };
        var results = await SolveAsync(SOLVE, packages);

        SmtResultPackageData Of(SmtTaskPackageData package)
        {
            return results.Single(result => result.Ticket == (SmtNodeRig.IsTicket(package.Ticket) ? package.Ticket : ""));
        }

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(packages.Count), "the work went on: every package has its answer");
            Assert.That(Of(good).FaultCode, Is.Empty);
            Assert.That(Of(good).ResultCount, Is.EqualTo(1));
            Assert.That(Of(version).FaultCode, Is.EqualTo(SmtNodeFaults.VERSION_MISMATCH));
            Assert.That(Of(corrupted).FaultCode, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE));
            Assert.That(Of(hashed).FaultCode, Is.EqualTo(SmtNodeFaults.PACKAGE_HASH_MISMATCH));
            Assert.That(Of(ticketless).FaultCode, Is.EqualTo(SmtNodeFaults.TICKET_INVALID));
            Assert.That(Of(oversized).FaultCode, Is.EqualTo(SmtNodeFaults.PACKAGE_TOO_LARGE));

            foreach (var refused in results.Where(result => result.FaultCode.Length > 0))
            {
                Assert.That(refused.ResultsBlobId, Is.EqualTo(Guid.Empty), refused.FaultCode);
                Assert.That(refused.ResultCount, Is.Zero, refused.FaultCode);
                Assert.That(refused.FaultMessage, Is.Not.Empty, refused.FaultCode);
            }
        });

        AssertNamesNoMachine([], results);
    }

    #endregion

    #region Size Tests

    [Test]
    public async Task APackageLargerThanOneChunkOfTheBlobChannelGoesAndComesTest()
    {
        // Problems whose text is mostly attribution the solver never sees: megabytes of letters
        // that do not compress, in a set-info, beside one small assertion each.
        const int PROBLEMS = 5;
        var problems = Enumerable.Range(0, PROBLEMS).Select(index => SmtSolverProblems.Check($"large-{index}", Large(index))).ToList();

        var package = await PublishAsync(problems);
        TestContext.Out.WriteLine($"the package: {problems.Count} problems, {problems.Sum(problem => (long)problem.Text.Length):N0} characters, a blob of {package.SizeBytes:N0} bytes");

        var results = await SolveAsync(SOLVE, [package]);
        var rows = await RowsAsync([package], results);

        Assert.Multiple(() =>
        {
            Assert.That(package.SizeBytes, Is.GreaterThan(BLOB_CHUNK_BYTES), "more than one chunk of the blob channel");
            Assert.That(results.Single().FaultCode, Is.Empty, results.Single().FaultMessage);
            Assert.That(rows, Has.Count.EqualTo(PROBLEMS));
            Assert.That(rows.Select(SmtNodeRig.Words), Has.All.EqualTo("ok sat"));
            Assert.That(rows.Select(row => row.Hash), Is.EquivalentTo(problems.Select(problem => problem.Hash)), "every text arrived as it was sent");
        });
    }

    #endregion

    #region Diagnostics Tests

    [Test]
    public async Task TheHostsModuleSaysWhatItCarriesAndCanDoTest()
    {
        var job = Engine.Compile(DIAGNOSTICS);
        var status = await Engine.ScheduleAndWaitAsync(job);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        var diagnostics = (SmtRuntimeDiagnosticsData)job.Variables["result"].Value!;
        using var capabilities = JsonDocument.Parse(await File.ReadAllBytesAsync(await Blobs.GetLocalPathAsync((Guid)job.Variables["capabilities"].Value!)));
        using var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(SmtRigModules.SolutionRoot()!, "AI", "OutWit.Controller.AI.Smt", "docs", "smt-capabilities.schema.json")));

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.ControllerVersion, Is.EqualTo(SmtNodeRig.ControllerVersion));
            Assert.That(diagnostics.ModulePresent, Is.True);
            Assert.That(diagnostics.ModuleCompiled, Is.True, "the rig's nodes run in this process, which has compiled the module");
            Assert.That(diagnostics.NativesPinned, Is.True);
            Assert.That(diagnostics.Platform, Is.EqualTo(SmtNodeRig.Platform));
            Assert.That(diagnostics.Degree, Is.InRange(1, SmtNodeRig.MaxDegree));

            Assert.That(SmtSchemaCheck.IsValid(schema.RootElement, capabilities.RootElement), Is.True, "the document is what its schema says");
            Assert.That(capabilities.RootElement.GetProperty("schema").GetString(), Is.EqualTo(SmtDiagnosticsRig.CapabilitiesSchema));
            Assert.That(capabilities.RootElement.GetProperty("pool").GetProperty("nodes").GetInt32(), Is.EqualTo(NODES), "the pool as totals");
            Assert.That(capabilities.RootElement.GetProperty("pool").GetProperty("measured").GetInt32(), Is.Zero, "the rig's nodes carry the default score");
        });

        var text = capabilities.RootElement.GetRawText() + diagnostics + JsonSerializer.Serialize(diagnostics);
        Assert.That(text, Does.Not.Contain(Environment.MachineName).IgnoreCase, "nothing names the machine");
        Assert.That(text, Does.Not.Contain(Environment.UserName).IgnoreCase, "or its user");
    }

    #endregion

    #region Tools

    private async Task<List<SmtTaskPackageData>> PublishGoldenAsync()
    {
        var packages = new List<SmtTaskPackageData>();
        foreach (var (options, problems) in SmtGoldenPackages.Groups())
            packages.Add(await SmtNodeRig.PublishAsync(problems, options, Blobs, WorkFolder));

        return packages;
    }

    private async Task<SmtTaskPackageData> PublishAsync(List<SmtProblemData> problems)
    {
        for (var index = 0; index < problems.Count; index++)
            problems[index].Index = index;

        return await SmtNodeRig.PublishAsync(problems, new SmtOptionsData(), Blobs, WorkFolder);
    }

    private async Task<List<SmtResultPackageData>> SolveAsync(string script, List<SmtTaskPackageData> packages)
    {
        var job = Engine.Compile(script);
        var status = await Engine.ScheduleAndWaitAsync(job, packages);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        return ((IEnumerable)job.Variables["results"].Value!).OfType<SmtResultPackageData>().ToList();
    }

    private async Task<List<SmtResultData>> RowsAsync(List<SmtTaskPackageData> packages, List<SmtResultPackageData> results)
    {
        var rows = new List<SmtResultData>();
        foreach (var result in results)
        {
            var package = packages.Single(one => one.Ticket == result.Ticket);
            Assert.That(result.FaultCode, Is.Empty, result.FaultMessage);
            Assert.That(result.ResultCount, Is.EqualTo(package.ProblemCount));

            var path = await Blobs.GetLocalPathAsync(result.ResultsBlobId);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(result.SizeBytes));

            var read = SmtNodeRig.ReadRows(path, package.ProblemCount);
            Assert.That(read.Select(row => row.Ticket), Has.All.EqualTo(package.Ticket), "every row answers its package");
            rows.AddRange(read);
        }

        return rows;
    }

    private static void AssertNamesNoMachine(List<SmtResultData> rows, List<SmtResultPackageData> results)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
            text.Append(row).Append(row.Message).Append(row.Transcript).Append(row.Platform).Append(string.Concat(row.Models)).Append(string.Concat(row.Values));

        foreach (var result in results)
            text.Append(result).Append(result.FaultMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text.ToString(), Does.Not.Contain(Environment.MachineName).IgnoreCase, "no row and no result names the machine");
            Assert.That(text.ToString(), Does.Not.Contain(Environment.UserName).IgnoreCase, "or its user");
            Assert.That(text.ToString(), Does.Not.Contain(Path.GetTempPath()).IgnoreCase, "or a folder of it");
        });
    }

    private static string Large(int seed)
    {
        const string LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        const int STRINGS = 64;
        const int CHARACTERS = 60_000;

        var random = new Random(seed);
        var text = new StringBuilder("(set-logic QF_LIA)\n(set-info :notes (");
        for (var item = 0; item < STRINGS; item++)
        {
            text.Append('"');
            for (var character = 0; character < CHARACTERS; character++)
                text.Append(LETTERS[random.Next(LETTERS.Length)]);
            text.Append("\" ");
        }

        return text.Append("))\n(declare-const x Int)\n(assert (> x ").Append(seed).Append("))\n").ToString();
    }

    #endregion
}
