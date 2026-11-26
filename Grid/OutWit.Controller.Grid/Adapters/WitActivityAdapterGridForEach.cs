using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Common.Serialization;
using OutWit.Controller.Grid.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Builders;
using OutWit.Controller.Grid.Interfaces;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Processing;
using System.Reflection;

namespace OutWit.Controller.Grid.Adapters
{
    internal class WitActivityAdapterGridForEach : WitActivityAdapterTransform<WitActivityGridForEach>,
        IWitActivityAdapter<WitActivityGridForEach>
    {
        #region Constants

        private const string ERROR_QUEUED_COPIES = "Grid.ForEach: copies need the Balanced strategy; the Queued strategy has no plan that keeps them on different machines.";

        private const string ERROR_RESULTS_NOT_TAKEN = "Grid.ForEach => {0}: the return collection '{1}' did not take the {2} value(s) of the call; it holds {3} value(s). Its elements are of type {4}, and the engine reported the list as {5}. None of what the nodes returned is stored.";

        private const string ERROR_FIRST_REFUSED = " The first value it would refuse is number {0} of the list: {1}.";

        #endregion

        #region Constructors

        public WitActivityAdapterGridForEach(IWitControllerManager controllerManager,
            IWitProcessingManager processingManager, IWitNodesManager nodesManager, IResources resources,
            ILogger logger)
            : base(controllerManager, processingManager, logger)
        {
            NodesManager = nodesManager;
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivityGridForEach activity, IWitVariablesCollection pool,
            IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Transformer == null)
                return;

            if (!pool.TryGetObject(activity.Collection, out object? collectionObject) ||
                collectionObject is not IEnumerable collection)
                throw this.FailedToGetParameterValueException(forEach => forEach.Collection);

            var iterationVariableName = activity.IterationVariable?.Reference;
            if (string.IsNullOrEmpty(iterationVariableName))
                throw this.FailedToGetParameterValueException(forEach => forEach.IterationVariable);

            var options = WitProcessingOptions.Default;

            if (pool.TryGetValue(activity.Options, out WitProcessingOptions? processingOptions) &&
                processingOptions != null)
                options = processingOptions;

            if (options.Replicas > 1 && options.Strategy == ProcessingStrategy.Queued)
                throw new InvalidOperationException(ERROR_QUEUED_COPIES);

            var benchmarkActivityType = activity.Transformer.GetType();

            IReadOnlyList<IWitEngineActivityNode> nodes
                = await NodesManager.GetCompatibleNodes(benchmarkActivityType, options);

            // A typed return collection takes a list whole or not at all. Every value a node returns is
            // therefore held to what the collection takes before it joins the results: one null or one
            // value of another type must cost the call that value, not all of them.
            //
            // A return collection may also ask to be told which item each value answers (it declares
            // OnGridAnswer). Its rule then tells it of every value that is kept, with the item of the
            // return slot the value came under. The slots and their items are written down while the
            // tasks are built, for such a collection only, and stay here on the host: a task carries
            // nothing of them.
            var slotItems = new Dictionary<string, object?>(StringComparer.Ordinal);

            GridValueRule values = GridValueRule.ForCollection(FindReturnCollection(pool, activity.ReturnReference), slotItems, LogWarning);

            IReadOnlyList<WitGridTask> tasks
                = BuildTasks(collection, activity.Transformer, pool, iterationVariableName, options, nodes, values.Tells ? slotItems : null);

            // A batch with copies must not be moved by the server: a substitute could be the machine
            // holding the other copy. Reassignment below decides where the work goes instead.
            bool exactNode = tasks.Any(task => task.ReplicaOf != null);

            var resultList = new List<object?>();

            try
            {
                if (options.Strategy == ProcessingStrategy.Queued)
                {
                    var (queuedStatus, queuedVariables) = await NodesManager.ProcessQueued(nodes, tasks, status.JobId, values, LogWarning);
                    status.AddChild(queuedStatus);

                    foreach (var variable in queuedVariables)
                        resultList.Add(variable.Value);
                }
                else
                {
                    var canRunInParallelOnClient = ResolveClientParallelPolicy(tasks);

                    // Fault-tolerant distribution: a node that FAILS its batch has its tasks reassigned to the
                    // remaining healthy nodes and retried, instead of failing the whole job because one machine
                    // died. Cancellation is honoured inside DistributeAsync (a node renders its whole batch
                    // before reporting; on cancel the orphaned groups finish in the background and are ignored).
                    // The job fails only when no node can complete the work — a genuine error surfaced with the
                    // last node's message.
                    var outcome = await GridReassignment.DistributeAsync(
                        nodes,
                        tasks,
                        async (group, _) =>
                        {
                            try
                            {
                                (IWitProcessingStatus groupStatus, IReadOnlyList<IWitVariable> groupVariables)
                                    = await NodesManager.Process(group, status.JobId, canRunInParallelOnClient, exactNode);

                                bool succeeded = groupStatus.Result != WitProcessingResult.Failed;
                                return new GridReassignment.GroupProcessResult(succeeded, groupStatus, groupVariables);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "Node {NodeId} threw while processing its task batch; treating it as a node failure.", group.Node.NodeId);
                                return new GridReassignment.GroupProcessResult(false, null, Array.Empty<IWitVariable>());
                            }
                        },
                        LogWarning,
                        values,
                        ProcessingManager.CancellationToken(status.JobId));

                    foreach (var groupStatus in outcome.CompletedStatuses)
                        status.AddChild(groupStatus);

                    if (outcome.Failed)
                        throw new InvalidOperationException(
                            outcome.FailureMessage ?? "Distributed processing failed on all available nodes.");

                    foreach (var value in outcome.Results)
                        resultList.Add(value);
                }
            }
            finally
            {
                // The method of a collection that asked to be told is the controller's code and may
                // throw. No value is lost for it; one warning a call says how often it threw, however
                // the call ends. A collection that did not ask has nothing to say here.
                values.WarnOfUntold(LogWarning);
            }

            StoreResults(activity, pool, resultList);
        }

        private void LogWarning(string message)
        {
            Logger.LogWarning("{Message}", message);
        }

        private IReadOnlyList<WitGridTask> BuildTasks(IEnumerable collection, IWitActivity transformer, IWitVariablesCollection pool,
            string iterationVariableName, WitProcessingOptions options, IReadOnlyList<IWitEngineActivityNode> nodes,
            IDictionary<string, object?>? slotItems)
        {
            if (options.Replicas <= 1)
                return ControllerManager.BuildTasks(collection, transformer, pool, iterationVariableName, slotItems);

            var items = collection.Cast<object>().ToList();
            int machines = nodes.Select(node => node.NodeId).Distinct().Count();

            IReadOnlyList<int>? slots = WitGridReplicaPlanner.Plan(items.Count, options.Replicas, options.ReplicaShare, machines);

            return slots == null
                ? ControllerManager.BuildTasks(items, transformer, pool, iterationVariableName, slotItems)
                : ControllerManager.BuildTasks(items, slots, transformer, pool, iterationVariableName, slotItems);
        }

        #endregion

        #region Results

        /// <summary>
        /// Finds the collection a call returns into: the variable under the return reference, when it
        /// is a collection. A call that is not assigned, and one assigned to a variable that is not a
        /// collection, have none.
        /// </summary>
        private static IWitCollection? FindReturnCollection(IWitVariablesCollection pool, string? returnReference)
        {
            if (string.IsNullOrEmpty(returnReference))
                return null;

            foreach (IWitVariable variable in pool)
            {
                if (string.Equals(variable.Name, returnReference, StringComparison.Ordinal))
                    return variable as IWitCollection;
            }

            return null;
        }

        /// <summary>
        /// Puts the results of a call into its return collection and fails the call when the
        /// collection does not hold them afterwards. Every value was held to what the collection takes
        /// before it got here, so a refusal now means the collection and that check disagree, and the
        /// script would go on with values it does not have (or with those of an earlier call). The
        /// collection is read back, because an engine before Data 1.1.6 answers "taken" whatever the
        /// collection did. A call with no return collection stores nothing, as before.
        /// </summary>
        private static void StoreResults(WitActivityGridForEach activity, IWitVariablesCollection pool, List<object?> results)
        {
            bool taken = pool.TrySetCollection(activity.ReturnReference, results);

            IWitCollection? collection = FindReturnCollection(pool, activity.ReturnReference);
            if (collection == null)
                return;

            List<object?> held = Held(collection);
            if (Same(held, results))
                return;

            GridValueRule values = GridValueRule.ForCollection(collection);

            string message = string.Format(ERROR_RESULTS_NOT_TAKEN,
                activity.Transformer?.GetOperatorType(),
                activity.ReturnReference,
                results.Count,
                held.Count,
                values.ElementType?.Name ?? nameof(Object),
                taken ? "taken" : "refused");

            int refused = results.FindIndex(value => !values.Accepts(value));
            if (refused >= 0)
                message += string.Format(ERROR_FIRST_REFUSED, refused + 1, GridValueRule.Describe(results[refused]));

            throw new InvalidOperationException(message);
        }

        private static List<object?> Held(IWitCollection collection)
        {
            var held = new List<object?>();

            if (collection.Value is IEnumerable values)
            {
                foreach (object? value in values)
                    held.Add(value);
            }

            return held;
        }

        private static bool Same(List<object?> held, List<object?> results)
        {
            if (held.Count != results.Count)
                return false;

            for (int index = 0; index < held.Count; index++)
            {
                if (!ReferenceEquals(held[index], results[index]) && !Equals(held[index], results[index]))
                    return false;
            }

            return true;
        }

        #endregion

        #region Parsing


        protected override WitActivityGridForEach CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

                    case 4:
                        return CreateActivity(parameters[0], parameters[1], parameters[2], parameters[3]);

                    default:
                        throw this.ParametersCountException(3);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
          
        }

        private WitActivityGridForEach CreateActivity(IWitParameter iterationVariable, IWitParameter keyword, IWitParameter collection)
        {
            if (iterationVariable is not IWitReference iterationVariableReference)
                throw this.ExpectedReferenceException(specialIf => specialIf.IterationVariable);

            if (keyword is not IWitCondition keywordValue || keywordValue.Condition?.ToLower() != "in")
                throw this.ExpectedConditionException(specialIf => specialIf.Keyword);

            if (!collection.IsArrayOrReference())
                throw this.ExpectedArrayException(specialIf => specialIf.Collection);

            return new WitActivityGridForEach
            {
                IterationVariable = iterationVariableReference,
                Keyword = keywordValue,
                Collection = collection
            };
        }

        private WitActivityGridForEach CreateActivity(IWitParameter iterationVariable, IWitParameter keyword,
            IWitParameter collection, IWitParameter options)
        {
            var activity = CreateActivity(iterationVariable, keyword, collection);

            if (options is not IWitReference optionsReference)
                throw this.ExpectedReferenceException(grid => grid.Options);

            return new WitActivityGridForEach
            {
                IterationVariable = activity.IterationVariable,
                Keyword = activity.Keyword,
                Collection = activity.Collection,
                Options = optionsReference
            };
        }

        private static bool ResolveClientParallelPolicy(IReadOnlyList<WitGridTask> tasks)
        {
            if (tasks.Count == 0)
                return false;

            foreach (var task in tasks)
            {
                var attr = task.Activity.GetType().GetCustomAttribute<CanRunInParallelOnClientAttribute>();
                if (attr?.CanRunInParallel != true)
                    return false;
            }

            return true;
        }

        #endregion


        public IWitNodesManager NodesManager { get; }
        
        public IResources Resources { get; }


    }
}
