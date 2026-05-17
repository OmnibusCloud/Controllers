using OutWit.Common;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Data.Variables;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialTrace : WitActivityAdapterCommand<WitActivitySpecialTrace>, IWitActivityAdapter<WitActivitySpecialTrace>
    {
        #region Constructors
        public WitActivityAdapterSpecialTrace(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivitySpecialTrace activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetString(activity.Message, out string? message))
                throw this.FailedToGetParameterValueException(trace => trace.Message);
            
            ProcessingManager.Trace(status.JobId, message ?? "");

            if (pool.TryGetValue(activity.ThrowException, out bool throwException) && throwException)
                throw this.ManuallyTriggeredException();
        }

        #endregion

        #region Parsing

        protected override WitActivitySpecialTrace CreateActivity(IWitParameter[] parameters)
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

        private WitActivitySpecialTrace CreateActivity(IWitParameter message)
        {
            return new WitActivitySpecialTrace
            {
                Message = message
            };
        }

        private WitActivitySpecialTrace CreateActivity(IWitParameter message, IWitParameter throwException)
        {
            if (!throwException.IsBooleanOrReference())
                throw this.ExpectedBooleanException(trace => trace.ThrowException);

            return new WitActivitySpecialTrace
            {
                Message = message,
                ThrowException = throwException
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
