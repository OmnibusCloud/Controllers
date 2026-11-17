using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// The rig's nodes with one liar among them: the first of the three does the work honestly (it is
/// the same sandbox) and then returns something else in place of every result package, as told.
/// What it returns is made by <see cref="Lie"/> from the honest reference; null leaves it honest.
/// </summary>
internal sealed class VerifyLyingNodesManager : IWitNodesManager
{
    #region Fields

    private readonly VerifyTestNodesManager m_inner;

    #endregion

    #region Constructors

    public VerifyLyingNodesManager(IWitEngineNode node)
    {
        m_inner = new VerifyTestNodesManager(node);
    }

    #endregion

    #region IWitNodesManager

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options)
        where TActivity : IWitActivity
    {
        return m_inner.GetCompatibleNodes<TActivity>(options);
    }

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
    {
        return m_inner.GetCompatibleNodes(activityType, options);
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity, IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
    {
        var (status, variables) = await m_inner.Process(nodeId, jobId, activity, pool, returnVariables);
        return (status, await Returned(nodeId, variables));
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId, IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
    {
        var (status, variables) = await m_inner.ProcessBatch(nodeId, jobId, requests, canRunInParallelOnClient);
        return (status, await Returned(nodeId, variables));
    }

    #endregion

    #region Tools

    private async Task<IReadOnlyList<IWitVariable>> Returned(Guid nodeId, IReadOnlyList<IWitVariable> variables)
    {
        var lie = Lie;
        if (lie == null || nodeId != Liar)
            return variables;

        var returned = new List<IWitVariable>(variables.Count);
        foreach (var variable in variables)
        {
            if (variable.Value is VerifyResultPackageData honest)
            {
                Lies++;
                returned.Add(new WitVariableVerifyResultPackage(variable.Name, await lie(honest)));
            }
            else
            {
                returned.Add(variable);
            }
        }

        return returned;
    }

    #endregion

    #region Properties

    /// <summary>What the liar returns in place of an honest result reference; null for an honest rig.</summary>
    public Func<VerifyResultPackageData, Task<VerifyResultPackageData>>? Lie { get; set; }

    /// <summary>The node that lies.</summary>
    public Guid Liar => m_inner.CompatibleNodes[0].NodeId;

    /// <summary>The result packages the liar replaced since the manager was made or the count was reset.</summary>
    public int Lies { get; set; }

    #endregion
}
