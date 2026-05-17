using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterBoolean : WitActivityAdapterFunction<WitActivityBoolean>, IWitActivityAdapter<WitActivityBoolean>
    {
        #region Constructors

        public WitActivityAdapterBoolean(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityBoolean activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Value, out bool value))
                throw this.FailedToGetParameterValueException(activityBoolean => activityBoolean.Value);
            
            bool result = pool.TrySetValue(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityBoolean CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (!parameters[0].IsBooleanOrReference())
                    throw this.ExpectedBooleanException(activityBoolean => activityBoolean.Value);

                return new WitActivityBoolean
                {
                    Value = parameters[0]
                };
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);

            }
        }

        #endregion

        public IResources Resources { get; }

    }
}
