using OutWit.Common;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Data.Variables;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialPause : WitActivityAdapterCommand<WitActivitySpecialPause>, IWitActivityAdapter<WitActivitySpecialPause>
    {
        #region Constructors

        public WitActivityAdapterSpecialPause(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivitySpecialPause activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Timeout, out int timeout))
                throw this.FailedToGetParameterValueException(pause => pause.Timeout);
            
            if(pool.TryGetValue(activity.Message, out string? message) && !string.IsNullOrEmpty(message))
                ProcessingManager.Trace(status.JobId, message);

            await Task.Delay(timeout, ProcessingManager.CancellationToken(status.JobId));
        }

        #endregion

        #region Parsing
        

        protected override WitActivitySpecialPause CreateActivity(IWitParameter[] parameters)
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
                        throw this.ParametersCountException(1, 2);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
           
        }

        private WitActivitySpecialPause CreateActivity(IWitParameter timeout)
        {
            if (!timeout.IsNumericOrReference())
                throw this.ExpectedNumericException(pause => pause.Timeout);
            
            return new WitActivitySpecialPause
            {
                Timeout = timeout
            };
        }

        private WitActivitySpecialPause CreateActivity(IWitParameter timeout, IWitParameter message)
        {
            if (!message.IsStringOrReference())
                throw this.ExpectedStringException(pause => pause.Message);

            if (!timeout.IsNumericOrReference())
                throw this.ExpectedNumericException(pause => pause.Timeout);
            
            return new WitActivitySpecialPause
            {
                Message = message,
                Timeout = timeout
            };
        }

        #endregion

        public IResources Resources { get; }
    }
}
