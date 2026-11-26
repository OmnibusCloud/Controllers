using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A nodes manager over the in-process node that writes down what a node is sent, at the moment
    /// it is sent: the activity, every variable of the pool with its value, the names the node is
    /// asked for, and for a batch whether it must run on exactly that node. The node then runs the
    /// request as an honest node does. The node itself is not written down: which node takes which
    /// task is the allocator's business.
    /// </summary>
    internal sealed class MockRecordingNodesManager : IWitNodesManager
    {
        #region Constructors

        static MockRecordingNodesManager()
        {
            // The in-process node is loaded once for the whole run, by the manager the other fixtures use.
            RuntimeHelpers.RunClassConstructor(typeof(MockNodesManager).TypeHandle);
        }

        #endregion

        #region Functions

        /// <summary>
        /// What a node is sent for one task, as one line of text.
        /// </summary>
        /// <param name="activity">The activity of the task.</param>
        /// <param name="pool">The variables of the task.</param>
        /// <param name="returnVariables">The names the node is asked for.</param>
        /// <param name="exactNode">Whether the batch must run on exactly its node; null for a task that travels alone.</param>
        /// <returns>The line.</returns>
        public static string Describe(IWitActivity activity, IWitVariablesCollection pool, IReadOnlyList<string> returnVariables, bool? exactNode)
        {
            // By name: the order a pool gives its variables in is the pool's own and says nothing.
            string variables = string.Join(", ", pool
                .OrderBy(variable => variable.Name, StringComparer.Ordinal)
                .Select(variable => $"{variable.Name}={Text(variable.Value)}"));
            string asked = string.Join(", ", returnVariables);
            string exact = exactNode == null ? "alone" : exactNode.Value ? "exact node" : "any node";

            return $"{activity.GetType().Name} {activity} | pool: {variables} | asks: {asked} | {exact}";
        }

        private static string Text(object? value)
        {
            return value switch
            {
                null => "null",
                string text => $"\"{text}\"",
                IEnumerable values => $"[{string.Join(", ", values.Cast<object?>().Select(Text))}]",
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

        private void Record(string line)
        {
            lock (Sent)
                Sent.Add(line);
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
            return Task.FromResult(CompatibleNodes);
        }

        public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity,
            IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
        {
            Record(Describe(activity, pool, returnVariables, null));

            return await WitEngineNodeSdk.Instance.Process(jobId, activity, pool, returnVariables);
        }

        public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId,
            IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
        {
            foreach (var request in requests)
                Record(Describe(request.Activity, request.Pool, request.ReturnVariables, request.ExactNode));

            var allVariables = new List<IWitVariable>();
            IWitProcessingStatus? lastStatus = null;

            foreach (var request in requests)
            {
                var (status, variables) = await WitEngineNodeSdk.Instance.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
                lastStatus = status;
                allVariables.AddRange(variables);

                if (status.Result == WitProcessingResult.Failed)
                    return (status, allVariables);
            }

            return (lastStatus ?? throw new InvalidOperationException("No requests provided"), allVariables);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Every task that was sent, one line each, in the order they were sent.
        /// </summary>
        public List<string> Sent { get; } = new();

        /// <summary>
        /// The nodes a call may use.
        /// </summary>
        public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; set; } = Array.Empty<IWitEngineActivityNode>();

        #endregion
    }
}
