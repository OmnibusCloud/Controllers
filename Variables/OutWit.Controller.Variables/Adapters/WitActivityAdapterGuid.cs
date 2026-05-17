using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterGuid : WitActivityAdapterFunction<WitActivityGuid>, IWitActivityAdapter<WitActivityGuid>
    {
        #region Constructors

        public WitActivityAdapterGuid(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityGuid activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result;

            if (activity.Value is WitConstantString constant && Guid.TryParse(constant.GetValue<string>(), out var guid))
                result = pool.TrySetValue<Guid?>(activity.ReturnReference, guid);
            else if (!pool.TryGetValue(activity.Value, out Guid? value))
                throw this.FailedToGetParameterValueException(activityGuid => activityGuid.Value);
            else
                result = pool.TrySetValue<Guid?>(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityGuid CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (!parameters[0].IsStringOrReference())
                    throw this.ExpectedStringException(activityGuid => activityGuid.Value);

                return new WitActivityGuid
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
