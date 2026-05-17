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
using System.Collections.Generic;
using System.Threading.Tasks;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialTransformForEach : WitActivityAdapterTransform<WitActivitySpecialTransformForEach>, IWitActivityAdapter<WitActivitySpecialTransformForEach>
    {
        #region Constants

        private const string RETURN_VARIABLE_NAME = "return";

        #endregion

        #region Constructors

        public WitActivityAdapterSpecialTransformForEach(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivitySpecialTransformForEach activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Transformer == null)
                return;

            if (!pool.TryGetObject(activity.Collection, out object? collectionObject) || collectionObject is not IEnumerable collection)
                throw this.FailedToGetParameterValueException(forEach => forEach.Collection);
            
            var iterationVariableName = activity.IterationVariable?.Reference;
            if(string.IsNullOrEmpty(iterationVariableName))
                throw this.FailedToGetParameterValueException(forEach => forEach.IterationVariable);

            if (activity.Transformer is IWitFunction function)
                function.SetReturnReference(RETURN_VARIABLE_NAME);
            

            var resultList = new List<object?>();

            IWitActivityStatus? childStatus = activityStatus?.Child();
            foreach (var value in collection)
            {
                if (status.IsFailed())
                    return;

                await ProcessingManager.WaitAsync(status.JobId);
                ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                var variables = BuildVariables(pool, iterationVariableName, value);

                status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, activity.Transformer, childStatus, variables, false));
                
                if(variables.TryGetObject(RETURN_VARIABLE_NAME, out object? result))
                    resultList.Add(result);
            }

            pool.TrySetCollection(activity.ReturnReference, resultList);
        }
        
        private IWitVariablesCollection BuildVariables(IWitVariablesCollection pool, string iterationVariableName, object value)
        {
            var variables = new WitVariableCollection
            {
                new WitVariableObject(RETURN_VARIABLE_NAME)
            };

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


        protected override WitActivitySpecialTransformForEach CreateActivity(IWitParameter[] parameters)
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

        private WitActivitySpecialTransformForEach CreateActivity(IWitParameter iterationVariable, IWitParameter keyword, IWitParameter collection)
        {
            if (iterationVariable is not IWitReference iterationVariableReference)
                throw this.ExpectedReferenceException(specialIf => specialIf.IterationVariable);

            if (keyword is not IWitCondition keywordValue || keywordValue.Condition?.ToLower() != "in")
                throw this.ExpectedConditionException(specialIf => specialIf.Keyword);

            if (!collection.IsArrayOrReference())
                throw this.ExpectedArrayException(specialIf => specialIf.Collection);

            return new WitActivitySpecialTransformForEach
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
