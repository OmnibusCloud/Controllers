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
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterDateTimeOffsetNow : WitActivityAdapterFunction<WitActivityDateTimeOffsetNow>, IWitActivityAdapter<WitActivityDateTimeOffsetNow>
    {
        #region Constructors

        public WitActivityAdapterDateTimeOffsetNow(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityDateTimeOffsetNow activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, DateTimeOffset.UtcNow);
            
            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityDateTimeOffsetNow CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 0)
                    throw this.ParametersCountException(0);

                return new WitActivityDateTimeOffsetNow();
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
