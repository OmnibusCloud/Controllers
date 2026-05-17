using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Threading.Tasks;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterProcessingOptions : WitActivityAdapterFunction<WitActivityProcessingOptions>, IWitActivityAdapter<WitActivityProcessingOptions>
    {
        #region Constructors

        public WitActivityAdapterProcessingOptions(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityProcessingOptions activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Reference != null)
                await ProcessFromReference(activity, pool, activityStatus, status);

            else
                await ProcessFromParameters(activity, pool, activityStatus, status);
        }
        
        protected async Task ProcessFromReference(WitActivityProcessingOptions activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result;
            
            if(pool.TryGetValue(activity.Reference, out WitProcessingOptions? processingOptions))
                result = pool.TrySetValue(activity.ReturnReference, processingOptions);
            else if(pool.TryGetValue(activity.Reference, out string? strategy) && ProcessingStrategy.TryParse(strategy, out ProcessingStrategy strategyValue))
                result = pool.TrySetValue(activity.ReturnReference, new WitProcessingOptions { Strategy = strategyValue});
            else
                throw this.FailedToGetParameterValueException(options => options.Reference);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        }

        protected async Task ProcessFromParameters(WitActivityProcessingOptions activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Strategy, out string? strategy) || !ProcessingStrategy.TryParse(strategy, out ProcessingStrategy strategyValue))
                throw this.FailedToGetParameterValueException(options => options.Strategy);

            int maxClients = -1;

            if (activity.MaxClients != null && !pool.TryGetValue(activity.MaxClients, out maxClients))
                throw this.FailedToGetParameterValueException(options => options.MaxClients);

            bool result = pool.TrySetValue(activity.ReturnReference, new WitProcessingOptions{Strategy = strategyValue, MaxClients = maxClients > 0? maxClients : null});

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Parsing

        protected override WitActivityProcessingOptions CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);
                    
                    case 2:
                        return CreateActivity(parameters[0], parameters[1]);

                    default:
                        throw this.ParametersCountException(1, 3, 4);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityProcessingOptions CreateActivity(IWitParameter single)
        {
            if (single is IWitReference reference)
                return new WitActivityProcessingOptions { Reference = reference };
            
            if(single.IsStringOrReference())
                return new WitActivityProcessingOptions { Strategy = single };

            throw this.ExpectedStringException(options => options.Reference);
        }

        private WitActivityProcessingOptions CreateActivity(IWitParameter strategy, IWitParameter maxClients)
        {
            if (!strategy.IsStringOrReference())
                throw this.ExpectedStringException(options => options.Strategy);

            if (!maxClients.IsNumericOrReference())
                throw this.ExpectedStringException(options => options.MaxClients);

            return new WitActivityProcessingOptions
            {
                Strategy = strategy,
                MaxClients = maxClients
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
