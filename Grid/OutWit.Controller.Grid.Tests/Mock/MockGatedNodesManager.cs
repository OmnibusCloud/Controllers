using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A nodes manager with no engine behind it whose nodes answer honestly, and all at the same
    /// moment: no task is answered before as many tasks wait as the gate says. The answers then come
    /// back on threads of their own, as the answers of real nodes do.
    /// </summary>
    internal sealed class MockGatedNodesManager : IWitNodesManager
    {
        #region Fields

        private readonly TaskCompletionSource m_open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly int m_gate;

        private int m_waiting;

        #endregion

        #region Constructors

        public MockGatedNodesManager(int gate)
        {
            m_gate = gate;
        }

        #endregion

        #region IWitNodesManager

        public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options)
            where TActivity : IWitActivity
        {
            return GetCompatibleNodes(typeof(TActivity), options);
        }

        public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
        {
            return Task.FromResult<IReadOnlyList<IWitEngineActivityNode>>(Array.Empty<IWitEngineActivityNode>());
        }

        public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity,
            IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
        {
            if (Interlocked.Increment(ref m_waiting) >= m_gate)
                m_open.TrySetResult();

            await m_open.Task;

            var request = new WitNodeTaskRequest { Activity = activity, Pool = pool, ReturnVariables = returnVariables };

            return (new WitProcessingStatus(nodeId, jobId).Completed(TimeSpan.Zero), MockAnsweringNodesManager.Honest(nodeId, request));
        }

        public Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId,
            IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
        {
            throw new NotSupportedException("The gate holds tasks that travel alone.");
        }

        #endregion
    }
}
