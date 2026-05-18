using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests.Mock
{
    internal class MockNodesManager : IWitNodesManager
    {
        static MockNodesManager()
        {
            WitEngineNodeSdk.Instance.Reload(false);
        }
        
        public async Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options) where TActivity : IWitActivity
        {
            LastRequestedActivityType = typeof(TActivity);
            return await Task.FromResult(CompatibleNodes);
        }

        public async Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
        {
            LastRequestedActivityType = activityType;
            return await Task.FromResult(CompatibleNodes);

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
            LastBatchRequests = requests
                .Select(request => new WitNodeTaskRequest
                {
                    Activity = request.Activity,
                    Pool = request.Pool,
                    ReturnVariables = request.ReturnVariables.ToArray()
                })
                .ToArray();

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

        public Type? LastRequestedActivityType { get; private set; }

        public IReadOnlyList<WitNodeTaskRequest>? LastBatchRequests { get; private set; }

        public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; set; }
            = new IWitEngineActivityNode[] { new MockActivityNode(WitEngineNodeSdk.Instance), new MockActivityNode(WitEngineNodeSdk.Instance), new MockActivityNode(WitEngineNodeSdk.Instance) };
    }
}
