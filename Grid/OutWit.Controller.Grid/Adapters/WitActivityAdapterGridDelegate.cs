using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Grid.Activities;
using OutWit.Controller.Grid.Builders;
using OutWit.Controller.Grid.Interfaces;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OutWit.Controller.Grid.Adapters
{
    internal class WitActivityAdapterGridDelegate : WitActivityAdapterTransform<WitActivityGridDelegate>,
        IWitActivityAdapter<WitActivityGridDelegate>
    {
        #region Constructors

        public WitActivityAdapterGridDelegate(IWitControllerManager controllerManager,
            IWitProcessingManager processingManager, IWitNodesManager nodesManager, IResources resources,
            ILogger logger)
            : base(controllerManager, processingManager, logger)
        {
            NodesManager = nodesManager;
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivityGridDelegate activity, IWitVariablesCollection pool,
            IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Transformer == null)
                return;

            var options = WitProcessingOptions.Default;

            if (pool.TryGetValue(activity.Options, out WitProcessingOptions? processingOptions) &&
                processingOptions != null)
                options = processingOptions;

            // Node compatibility is keyed off the TRANSFORMER's type (the activity that
            // actually runs on the node), exactly like Grid.ForEach.
            var benchmarkActivityType = activity.Transformer.GetType();

            IReadOnlyList<IWitEngineActivityNode> nodes
                = await NodesManager.GetCompatibleNodes(benchmarkActivityType, options);

            if (nodes.Count == 0)
                throw new InvalidOperationException("Grid.Delegate: no compatible nodes available.");

            // One task (the transformer scoped to the referenced pool subset), allocated
            // to a single node. WitGridTaskAllocator orders nodes by benchmark rate and
            // fills the fastest first, so a single task lands on the fastest compatible
            // node — i.e. "the most suitable node".
            WitGridTask task = ControllerManager.BuildTask(activity.Transformer, pool);

            IReadOnlyList<WitGridTaskGroup> groups = WitGridTaskAllocator.Allocate(nodes, new[] { task });

            WitGridTaskGroup group = groups.First();

            // A node runs its whole assigned work before reporting, so honour cancellation
            // here (mirrors Grid.ForEach): on cancel the engine finalizes the job as
            // Cancelled and the abandoned node task is observed in the background.
            var groupTask = NodesManager.Process(group, status.JobId, inParallel: false);
            try
            {
                await groupTask.WaitAsync(ProcessingManager.CancellationToken(status.JobId));
            }
            catch (OperationCanceledException)
            {
                _ = groupTask.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                throw;
            }

            (IWitProcessingStatus groupStatus, IReadOnlyList<IWitVariable> groupVariables) = groupTask.Result;
            status.AddChild(groupStatus);

            // The delegated activity returns a single value (unlike ForEach's collection).
            var resultVariable = groupVariables.FirstOrDefault();
            if (resultVariable != null && !string.IsNullOrEmpty(activity.ReturnReference))
                pool.TrySetValue(activity.ReturnReference, resultVariable.Value);
        }

        #endregion

        #region Parsing

        protected override WitActivityGridDelegate CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 0:
                        return new WitActivityGridDelegate();

                    case 1:
                        if (parameters[0] is not IWitReference optionsReference)
                            throw this.ExpectedReferenceException(grid => grid.Options);

                        return new WitActivityGridDelegate
                        {
                            Options = optionsReference
                        };

                    default:
                        throw this.ParametersCountException(0, 1);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
        }

        #endregion

        #region Properties

        public IWitNodesManager NodesManager { get; }

        public IResources Resources { get; }

        #endregion
    }
}
