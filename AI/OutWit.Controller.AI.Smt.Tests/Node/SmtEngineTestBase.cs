using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.AI.Smt.Tests.Node;

/// <summary>
/// The engine with in-process nodes, as Verify's script tests have it: the engine SDK host with the
/// modules a package needs (<see cref="SmtRigModules.ENGINE"/>: Smt, Variables, Grid), two
/// in-process nodes of their own ids (<see cref="SmtTestNodesManager"/>), and a blob store of files
/// shared by the host and the nodes. The nodes really solve: the fixture needs the solver module.
/// </summary>
public abstract class SmtEngineTestBase
{
    #region Constants

    /// <summary>The in-process nodes of the rig.</summary>
    protected const int NODES = 2;

    #endregion

    #region Fields

    private string m_storagePath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void EngineSetUp()
    {
        SmtSolverModule.Require();

        var modules = SmtRigModules.Stage("engine", SmtRigModules.ENGINE);

        m_storagePath = Path.Combine(Path.GetTempPath(), $"witai_smt_engine_{Guid.NewGuid():N}");
        Blobs = new SmtTestBlobService(Path.Combine(m_storagePath, "blobs"));
        WorkFolder = Path.Combine(m_storagePath, "work");
        Directory.CreateDirectory(WorkFolder);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: modules,
            configureServices: services => services.AddSingleton<IWitBlobService>(Blobs));

        Nodes = new SmtTestNodesManager(WitEngineNodeSdk.Instance, NODES);
        Engine = WitEngineSdk.Instance;
        Engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: modules,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(Blobs);
                services.AddSingleton<IWitNodesManager>(Nodes);
            });

        SmtSolverRig.Warm();
    }

    [OneTimeTearDown]
    public void EngineTearDown()
    {
        SmtRigModules.TryDelete(m_storagePath);
    }

    #endregion

    #region Properties

    /// <summary>The engine host.</summary>
    protected IWitEngine Engine { get; private set; } = null!;

    /// <summary>The blob store of the host and the nodes.</summary>
    private protected SmtTestBlobService Blobs { get; private set; } = null!;

    /// <summary>The in-process nodes.</summary>
    private protected SmtTestNodesManager Nodes { get; private set; } = null!;

    /// <summary>A folder of the fixture's own, for the packages it writes.</summary>
    protected string WorkFolder { get; private set; } = null!;

    #endregion
}
