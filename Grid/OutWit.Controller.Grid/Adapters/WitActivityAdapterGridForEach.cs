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

            var benchmarkActivityType = activity.Transformer.GetType();

            IReadOnlyList<IWitEngineActivityNode> nodes
                = await NodesManager.GetCompatibleNodes(benchmarkActivityType, options);

            IReadOnlyList<WitGridTask> tasks
                = ControllerManager.BuildTasks(collection, activity.Transformer, pool, iterationVariableName);

            var resultList = new List<object?>();

            if (options.Strategy == ProcessingStrategy.Queued)
            {
                var (queuedStatus, queuedVariables) = await NodesManager.ProcessQueued(nodes, tasks, status.JobId);
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
                                = await NodesManager.Process(group, status.JobId, canRunInParallelOnClient);

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
                    message => Logger.LogWarning("{Message}", message),
                    ProcessingManager.CancellationToken(status.JobId));

                foreach (var groupStatus in outcome.CompletedStatuses)
                    status.AddChild(groupStatus);

                if (outcome.Failed)
                    throw new InvalidOperationException(
                        outcome.FailureMessage ?? "Distributed processing failed on all available nodes.");

                foreach (var value in outcome.Results)
                    resultList.Add(value);
            }
            
            pool.TrySetCollection(activity.ReturnReference, resultList);
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
