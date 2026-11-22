using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A nodes manager with no engine behind it: the test says what a node returns for a request, and
    /// every request is kept as it came. An answer of null is a node that fails the request.
    /// </summary>
    internal sealed class MockAnsweringNodesManager : IWitNodesManager
    {
        #region Constants

        public const string FAILURE = "node failed";

        #endregion

        #region Fields

        private readonly Func<Guid, WitNodeTaskRequest, IReadOnlyList<IWitVariable>?> m_answer;

        #endregion

        #region Constructors

        public MockAnsweringNodesManager(Func<Guid, WitNodeTaskRequest, IReadOnlyList<IWitVariable>?> answer)
        {
            m_answer = answer;
        }

        #endregion

        #region Functions

        /// <summary>
        /// What the engine's node returns for a request: the variables of the pool under the names the
        /// request asks for. A variable with no value gets one that says who made it and under which name.
        /// </summary>
        /// <param name="nodeId">The node that answers.</param>
        /// <param name="request">The request.</param>
        /// <returns>The answer of an honest node.</returns>
        public static List<IWitVariable> Honest(Guid nodeId, WitNodeTaskRequest request)
        {
            return request.Pool
                .Where(variable => request.ReturnVariables.Contains(variable.Name))
                .Select(variable => (IWitVariable)new WitVariableObject(variable.Name, variable.Value ?? Answer(nodeId, variable.Name)))
                .ToList();
        }

        /// <summary>
        /// The value an honest node gives a slot.
        /// </summary>
        /// <param name="nodeId">The node that answers.</param>
        /// <param name="name">The name the value comes under.</param>
        /// <returns>The value.</returns>
        public static string Answer(Guid nodeId, string name)
        {
            return $"{nodeId:N}:{name}";
        }

        private IWitProcessingStatus Status(Guid nodeId, Guid jobId, bool completed)
        {
            var status = new WitProcessingStatus(StatusEngineId ?? nodeId, jobId);

            return completed
                ? status.Completed(TimeSpan.Zero)
                : status.Failed(TimeSpan.Zero, FAILURE);
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

        public Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity,
            IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
        {
            var request = new WitNodeTaskRequest
            {
                Activity = activity,
                Pool = pool,
                ReturnVariables = returnVariables.ToArray()
            };

            lock (Requests)
                Requests.Add((nodeId, request));

            IReadOnlyList<IWitVariable>? answer = m_answer(nodeId, request);

            (IWitProcessingStatus, IReadOnlyList<IWitVariable>) result = answer == null
                ? (Status(nodeId, jobId, false), Array.Empty<IWitVariable>())
                : (Status(nodeId, jobId, true), answer);

            return Task.FromResult(result);
        }

        public Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId,
            IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
        {
            var variables = new List<IWitVariable>();
            bool completed = true;

            foreach (var request in requests)
            {
                lock (Requests)
                    Requests.Add((nodeId, request));

                IReadOnlyList<IWitVariable>? answer = m_answer(nodeId, request);
                if (answer == null)
                {
                    completed = false;
                    break;
                }

                variables.AddRange(answer);
            }

            (IWitProcessingStatus, IReadOnlyList<IWitVariable>) result = (Status(nodeId, jobId, completed), variables);

            return Task.FromResult(result);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Every request that came, with the node it was sent to.
        /// </summary>
        public List<(Guid NodeId, WitNodeTaskRequest Request)> Requests { get; } = new();

        /// <summary>
        /// The engine id the returned statuses carry; the id of the node the request named when null.
        /// </summary>
        public Guid? StatusEngineId { get; set; }

        #endregion
    }
}
