using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Tests.Mock
{
    internal class MockNodesManager : IWitNodesManager
    {
        static MockNodesManager()
        {
            WitEngineNodeSdk.Instance.Reload(false);
        }
        
        public async Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options) where TActivity : IWitActivity
        {
            return await Task.FromResult(new IWitEngineActivityNode[] { new MockActivityNode(Node), new MockActivityNode(Node), new MockActivityNode(Node) });
        }

        public async Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
        {
            return await Task.FromResult(new IWitEngineActivityNode[] { new MockActivityNode(Node), new MockActivityNode(Node), new MockActivityNode(Node) });

        }

        public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity, IWitVariablesCollection pool,
            IReadOnlyList<string> returnVariables)
        {
            return await Node.Process(jobId, activity, pool, returnVariables);
        }

        public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(
            Guid nodeId,
            Guid jobId,
            IReadOnlyList<WitNodeTaskRequest> requests,
            bool canRunInParallelOnClient)
        {
            var allVariables = new List<IWitVariable>();
            IWitProcessingStatus? lastStatus = null;

            foreach (var request in requests)
            {
                var (status, variables) = await Node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
                lastStatus = status;
                allVariables.AddRange(variables);

                if (status.Result == WitProcessingResult.Failed)
                    return (status, allVariables);
            }

            return (lastStatus ?? throw new InvalidOperationException("No requests provided"), allVariables);
        }

        public IWitEngineNode Node => WitEngineNodeSdk.Instance;
    }
}
