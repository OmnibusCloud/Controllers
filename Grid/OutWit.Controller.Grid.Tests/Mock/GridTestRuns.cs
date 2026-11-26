using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// The tasks of one call run without an engine, under either strategy, the way the Grid.ForEach
    /// adapter runs them: Balanced as batches through the reassignment loop, Queued one task after
    /// another from the queue. What comes back is what the call would store.
    /// </summary>
    internal static class GridTestRuns
    {
        #region Constants

        public const string BALANCED = "Balanced";

        public const string QUEUED = "Queued";

        #endregion

        #region Functions

        /// <summary>
        /// Runs the tasks and returns the values that are kept, in the order the call stores them.
        /// </summary>
        /// <param name="strategy">The strategy: <see cref="BALANCED"/> or <see cref="QUEUED"/>.</param>
        /// <param name="manager">The nodes.</param>
        /// <param name="nodes">The nodes the call may use.</param>
        /// <param name="tasks">The tasks of the call.</param>
        /// <param name="values">The rule of the return collection; null when there is none.</param>
        /// <param name="warnings">Where the warnings go; null when nobody listens.</param>
        /// <returns>The kept values.</returns>
        public static async Task<List<object?>> RunAsync(string strategy, IWitNodesManager manager, IReadOnlyList<IWitEngineActivityNode> nodes,
            IReadOnlyList<WitGridTask> tasks, GridValueRule? values, List<string>? warnings = null)
        {
            Action<string>? logWarning = warnings == null ? null : message => Add(warnings, message);
            Guid jobId = Guid.NewGuid();

            if (strategy == QUEUED)
            {
                var (_, variables) = await manager.ProcessQueued(nodes, tasks, jobId, values, logWarning);

                return variables.Select(variable => variable.Value).ToList();
            }

            var outcome = await GridReassignment.DistributeAsync(
                nodes,
                tasks,
                async (group, _) =>
                {
                    var (status, variables) = await manager.Process(group, jobId, inParallel: false, exactNode: false);

                    return new GridReassignment.GroupProcessResult(status.Result != WitProcessingResult.Failed, status, variables);
                },
                logWarning,
                values,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.False, outcome.FailureMessage);

            return outcome.Results;
        }

        private static void Add(List<string> warnings, string message)
        {
            lock (warnings)
                warnings.Add(message);
        }

        #endregion
    }
}
