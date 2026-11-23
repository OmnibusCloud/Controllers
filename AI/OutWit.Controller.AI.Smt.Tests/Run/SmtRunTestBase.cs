using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// The bundled scripts through the engine with in-process nodes, as Verify's script tests have
/// them: the engine SDK host with the modules a run needs (<see cref="SmtRigModules.ENGINE"/>),
/// three in-process nodes of their own ids, any of which may lie (<see cref="SmtLyingNodesManager"/>),
/// and a blob store of files shared by the host and the nodes. The nodes really solve: the fixture
/// needs the solver module.
/// </summary>
public abstract class SmtRunTestBase
{
    #region Constants

    private const string SCRIPTS_FOLDER = "Scripts";

    #endregion

    #region Fields

    private string m_storagePath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void RunSetUp()
    {
        SmtSolverModule.Require();

        // A folder of the fixture's own: a module another fixture loaded from its folder stays
        // mapped, and the loader takes an assembly it already holds by its name.
        var modules = SmtRigModules.Stage(GetType().Name, SmtRigModules.ENGINE);

        m_storagePath = Path.Combine(Path.GetTempPath(), $"witai_smt_run_{Guid.NewGuid():N}");
        Blobs = new SmtTestBlobService(Path.Combine(m_storagePath, "blobs"));
        WorkFolder = Path.Combine(m_storagePath, "work");
        Directory.CreateDirectory(WorkFolder);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: modules,
            configureServices: services => services.AddSingleton<IWitBlobService>(Blobs));

        Nodes = CreateNodesManager(WitEngineNodeSdk.Instance);
        Engine = WitEngineSdk.Instance;
        Engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: modules,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(Blobs);
                services.AddSingleton(Nodes);
            });

        SmtSolverRig.Warm();
    }

    [OneTimeTearDown]
    public void RunTearDown()
    {
        SmtRigModules.TryDelete(m_storagePath);
    }

    #endregion

    #region Tools

    /// <summary>The rig's nodes: three that may lie, unless a fixture brings its own.</summary>
    /// <param name="node">The in-process node every one of them runs on.</param>
    /// <returns>The nodes manager the engine fans out through.</returns>
    private protected virtual IWitNodesManager CreateNodesManager(IWitEngineNode node)
    {
        return Liars = new SmtLyingNodesManager(node);
    }

    /// <summary>A bundled script, by file name.</summary>
    /// <param name="fileName">The script's file name.</param>
    /// <returns>Its text.</returns>
    protected static string Script(string fileName)
    {
        var path = Path.Combine(SmtRigModules.SolutionRoot()!, "AI", "OutWit.Controller.AI.Smt", SCRIPTS_FOLDER, fileName);
        if (!File.Exists(path))
            SmtRigModules.Missing($"{fileName} not found at {path}");

        return File.ReadAllText(path);
    }

    /// <summary>Stores a text as a blob.</summary>
    /// <param name="text">The text.</param>
    /// <param name="name">The blob's file name.</param>
    /// <returns>The blob's id.</returns>
    protected async Task<Guid> UploadAsync(string text, string name)
    {
        return await Blobs.UploadBytesAsync(Encoding.UTF8.GetBytes(text), name);
    }

    /// <summary>Runs SmtRun on a set and reads its documents.</summary>
    /// <param name="set">The set's text.</param>
    /// <param name="options">The run's options.</param>
    /// <returns>The run.</returns>
    private protected async Task<SmtRunDocuments> RunAsync(string set, SmtOptionsData options)
    {
        var job = Engine.Compile(Script("SmtRun.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(set, "problems.jsonl"), options);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        return await SmtRunDocuments.ReadAsync(job, Blobs);
    }

    #endregion

    #region Properties

    /// <summary>The engine host.</summary>
    protected IWitEngine Engine { get; private set; } = null!;

    /// <summary>The blob store of the host and the nodes.</summary>
    private protected SmtTestBlobService Blobs { get; private set; } = null!;

    /// <summary>The rig's nodes.</summary>
    private protected IWitNodesManager Nodes { get; private set; } = null!;

    /// <summary>The rig's nodes when they are the ones that may lie.</summary>
    private protected SmtLyingNodesManager Liars { get; private set; } = null!;

    /// <summary>A folder of the fixture's own.</summary>
    protected string WorkFolder { get; private set; } = null!;

    #endregion
}
