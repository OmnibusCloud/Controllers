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
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterString : WitActivityAdapterFunction<WitActivityString>, IWitActivityAdapter<WitActivityString>
    {
        #region Constructors

        public WitActivityAdapterString(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityString activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Value, out string? value))
                throw this.FailedToGetParameterValueException(activityString => activityString.Value);
            
            bool result = pool.TrySetValue<string?>(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityString CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (!parameters[0].IsStringOrReference())
                    throw this.ExpectedStringException(activityString => activityString.Value);

                return new WitActivityString
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
