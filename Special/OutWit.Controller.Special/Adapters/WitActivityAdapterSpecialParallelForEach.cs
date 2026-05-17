using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Common.Serialization;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialParallelForEach : WitActivityAdapterComposite<WitActivitySpecialParallelForEach>, IWitActivityAdapter<WitActivitySpecialParallelForEach>
    {
        #region Constructors

        public WitActivityAdapterSpecialParallelForEach(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task ProcessInner(WitActivitySpecialParallelForEach activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status, bool reportProgress)
        {
            if (activity.Activities.Count == 0)
                return;

            if (!pool.TryGetObject(activity.Collection, out object? collectionObject) || collectionObject is not IEnumerable collection)
                throw this.FailedToGetParameterValueException(forEach => forEach.Collection);
            
            var iterationVariableName = activity.IterationVariable?.Reference;
            if(string.IsNullOrEmpty(iterationVariableName))
                throw this.FailedToGetParameterValueException(forEach => forEach.IterationVariable);
            
            await Parallel.ForEachAsync(collection.Cast<object>(), ProcessingManager.CancellationToken(status.JobId), async (value, token) =>
            {
                var variables = BuildVariables(pool, iterationVariableName, value);

                foreach (var childActivity in activity.Activities)
                {
                    if (status.IsFailed())
                        return;

                    await ProcessingManager.WaitAsync(status.JobId);
                    ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                    status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, childActivity, activityStatus, variables, false));
                }
            });
        }
        
        private IWitVariablesCollection BuildVariables(IWitVariablesCollection pool, string iterationVariableName, object value)
        {
            var variables = new WitVariableCollection();

            if (value is string || value is not IEnumerable collection)
                variables.Add(new WitVariableObject(iterationVariableName, value));
            else
            {
                int i = 1;
                foreach(object? item in collection)
                    variables.Add(new WitVariableObject($"{iterationVariableName}.Item{i++}", item));
            }
            
            return variables.Join(pool);
        }

        #endregion

        #region Parsing


        protected override WitActivitySpecialParallelForEach CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

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

        private WitActivitySpecialParallelForEach CreateActivity(IWitParameter iterationVariable, IWitParameter keyword, IWitParameter collection)
        {
            if (iterationVariable is not IWitReference iterationVariableReference)
                throw this.ExpectedReferenceException(specialIf => specialIf.IterationVariable);

            if (keyword is not IWitCondition keywordValue || keywordValue.Condition?.ToLower() != "in")
                throw this.ExpectedConditionException(specialIf => specialIf.Keyword);

            if (!collection.IsArrayOrReference())
                throw this.ExpectedArrayException(specialIf => specialIf.Collection);

            return new WitActivitySpecialParallelForEach
            {
                IterationVariable = iterationVariableReference,
                Keyword = keywordValue,
                Collection = collection
            };
        }

        #endregion


        public IResources Resources { get; }
    }
}
