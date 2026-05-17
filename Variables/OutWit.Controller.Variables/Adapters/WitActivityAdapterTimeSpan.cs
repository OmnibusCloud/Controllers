using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Globalization;
using System.Threading.Tasks;
using OutWit.Engine.Data.Constants;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterTimeSpan : WitActivityAdapterFunction<WitActivityTimeSpan>, IWitActivityAdapter<WitActivityTimeSpan>
    {
        #region Constructors

        public WitActivityAdapterTimeSpan(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivityTimeSpan activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Value != null)
                await ProcessFromValue(activity, pool, activityStatus, status);

            else
                await ProcessFromParameters(activity, pool, activityStatus, status);
        }

        protected async Task ProcessFromValue(WitActivityTimeSpan activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result;

            if (activity.Value is WitConstantString constant &&
                TimeSpan.TryParse(constant.GetValue<string>(), CultureInfo.InvariantCulture, out var timeSpan))
                result = pool.TrySetValue<TimeSpan?>(activity.ReturnReference, timeSpan);
            else if (!pool.TryGetValue(activity.Value, out TimeSpan? value))
                throw this.FailedToGetParameterValueException(activityDateTime => activityDateTime.Value);
            else
                result = pool.TrySetValue<TimeSpan?>(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        }

        protected async Task ProcessFromParameters(WitActivityTimeSpan activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Hours, out int hours) || hours < 0)
                throw this.FailedToGetParameterValueException(timeSpan => timeSpan.Hours);

            if (!pool.TryGetValue(activity.Minutes, out int minutes) || minutes < 0)
                throw this.FailedToGetParameterValueException(timeSpan => timeSpan.Minutes);

            if (!pool.TryGetValue(activity.Seconds, out int seconds) || seconds < 0)
                throw this.FailedToGetParameterValueException(timeSpan => timeSpan.Seconds);

            bool result;
            
            if(!pool.TryGetValue(activity.Days, out int days) || days < 0)
                result = pool.TrySetValue<TimeSpan?>(activity.ReturnReference, new TimeSpan(hours, minutes, seconds));

            else if(!pool.TryGetValue(activity.Milliseconds, out int milliseconds) || milliseconds < 0)
                result = pool.TrySetValue<TimeSpan?>(activity.ReturnReference, new TimeSpan(days, hours, minutes, seconds));

            else
                result = pool.TrySetValue<TimeSpan?>(activity.ReturnReference, new TimeSpan(days, hours, minutes, seconds, milliseconds));

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        } 

        #endregion

        #region Parsing

        protected override WitActivityTimeSpan CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);
                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);
                    case 4:
                        return CreateActivity(parameters[0], parameters[1], parameters[2], parameters[3]);
                    case 5:
                        return CreateActivity(parameters[0], parameters[1], parameters[2], parameters[3], parameters[4]);

                    default:
                        throw this.ParametersCountException(3, 4, 5);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
         
        }

        private WitActivityTimeSpan CreateActivity(IWitParameter value)
        {
            if (!value.IsStringOrReference())
                throw this.ExpectedStringException(timeSpan => timeSpan.Value);

            return new WitActivityTimeSpan
            {
                Value = value,
            };
        }

        private WitActivityTimeSpan CreateActivity(IWitParameter hours, IWitParameter minutes, IWitParameter seconds)
        {
            if (!hours.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Hours);

            if (!minutes.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Minutes);

            if (!seconds.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Seconds);

            return new WitActivityTimeSpan
            {
                Hours = hours,
                Minutes = minutes,
                Seconds = seconds
            };
        }

        private WitActivityTimeSpan CreateActivity(IWitParameter days, IWitParameter hours, IWitParameter minutes, IWitParameter seconds)
        {
            if (!days.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Days);

            if (!hours.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Hours);

            if (!minutes.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Minutes);

            if (!seconds.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Seconds);

            return new WitActivityTimeSpan
            {
                Days = days,
                Hours = hours,
                Minutes = minutes,
                Seconds = seconds
            };
        }

        private WitActivityTimeSpan CreateActivity(IWitParameter days, IWitParameter hours, IWitParameter minutes, IWitParameter seconds, IWitParameter milliseconds)
        {
            if (!days.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Days);

            if (!hours.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Hours);

            if (!minutes.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Minutes);

            if (!seconds.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Seconds);

            if (!milliseconds.IsNumericOrReference())
                throw this.ExpectedNumericException(timeSpan => timeSpan.Milliseconds);

            return new WitActivityTimeSpan
            {
                Days = days,
                Hours = hours,
                Minutes = minutes,
                Seconds = seconds,
                Milliseconds = milliseconds
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
