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
using OutWit.Engine.Data.Variables;
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
        #region Constants

        private const string WARNING_VALUE_NOT_TAKEN = "{0} returned a value the return variable '{1}' did not take ({2}); the variable keeps the value it had.";

        #endregion

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

            // The delegated activity returns a single value (unlike ForEach's collection): the one
            // under the slot the node was asked for. Whatever else the node returned is left out, as
            // it is for a Grid.ForEach task.
            string who = GridAnswers.Who(group.Node.NodeId, groupStatus, "task");

            IReadOnlyList<IWitVariable> answers = GridAnswers.Keep(group, groupVariables, null, out int unasked, out var refused);

            GridAnswers.Warn(LogWarning, who, group.Count, unasked, refused, null);

            var resultVariable = answers.FirstOrDefault();
            if (resultVariable != null && !string.IsNullOrEmpty(activity.ReturnReference))
                StoreResult(pool, activity.ReturnReference, resultVariable.Value, who);
        }

        /// <summary>
        /// Puts the delegated activity's value into the return variable, as before, and says so when
        /// the variable did not take it: a typed variable refuses a value of another type (and a
        /// variable of a value type refuses null) and keeps the value it had. The variable is read
        /// back, because the engine answers "set" whatever the variable did. Nothing fails for it.
        /// </summary>
        private void StoreResult(IWitVariablesCollection pool, string returnReference, object? value, string who)
        {
            pool.TrySetValue(returnReference, value);

            IWitVariable? variable = FindVariable(pool, returnReference);
            if (variable == null)
                return;

            object? held = variable.Value;
            if (ReferenceEquals(held, value) || Equals(held, value))
                return;

            Type? expected = ValueTypeOf(variable);

            LogWarning(string.Format(WARNING_VALUE_NOT_TAKEN,
                who,
                returnReference,
                expected == null
                    ? GridValueRule.Describe(value)
                    : $"{GridValueRule.Describe(value)} where {expected.Name} was expected"));
        }

        private static IWitVariable? FindVariable(IWitVariablesCollection pool, string name)
        {
            foreach (IWitVariable variable in pool)
            {
                if (string.Equals(variable.Name, name, StringComparison.Ordinal))
                    return variable;
            }

            return null;
        }

        /// <summary>
        /// The type of value a variable holds, read from the engine's typed variable it derives from;
        /// null when it derives from none.
        /// </summary>
        private static Type? ValueTypeOf(IWitVariable variable)
        {
            for (Type? type = variable.GetType(); type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WitVariable<>))
                {
                    Type valueType = type.GetGenericArguments()[0];

                    return Nullable.GetUnderlyingType(valueType) ?? valueType;
                }
            }

            return null;
        }

        private void LogWarning(string message)
        {
            Logger.LogWarning("{Message}", message);
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
