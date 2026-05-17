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
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Engine.Data.Constants;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterDateTimeOffset : WitActivityAdapterFunction<WitActivityDateTimeOffset>, IWitActivityAdapter<WitActivityDateTimeOffset>
    {
        #region Constructors

        public WitActivityAdapterDateTimeOffset(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing


        protected override async Task Process(WitActivityDateTimeOffset activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Value != null)
                await ProcessFromValue(activity, pool, activityStatus, status);

            else
                await ProcessFromParameters(activity, pool, activityStatus, status);
        }

        protected async Task ProcessFromValue(WitActivityDateTimeOffset activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result;

            if (activity.Value is WitConstantString constant &&
                DateTimeOffset.TryParse(constant.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, dateTime);
            else if (!pool.TryGetValue(activity.Value, out DateTimeOffset? value))
                throw this.FailedToGetParameterValueException(activityDateTime => activityDateTime.Value);
            else
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        }

        protected async Task ProcessFromParameters(WitActivityDateTimeOffset activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            
            if (!pool.TryGetValue(activity.Year, out int year) || year < 0)
                throw this.FailedToGetParameterValueException(timeSpan => timeSpan.Year);

            if (!pool.TryGetValue(activity.Month, out int month) || month < 0)
                throw this.FailedToGetParameterValueException(timeSpan => timeSpan.Month);

            if (!pool.TryGetValue(activity.Day, out int day) || day < 0)
                throw this.FailedToGetParameterValueException(timeSpan => timeSpan.Day);

            bool result;

            if (!pool.TryGetValue(activity.Hour, out int hour) || hour < 0)
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, new DateTimeOffset(year, month, day, 0, 0,0, TimeSpan.Zero));

            else if (!pool.TryGetValue(activity.Minute, out int minute) || minute < 0)
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));

            else if (!pool.TryGetValue(activity.Second, out int second) || second < 0)
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));

            else if (!pool.TryGetValue(activity.Offset, out TimeSpan? offset))
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

            else
                result = pool.TrySetValue<DateTimeOffset?>(activity.ReturnReference, new DateTimeOffset(year, month, day, hour, minute, second, offset!.Value));

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        } 

        #endregion

        #region Parsing

        protected override WitActivityDateTimeOffset CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);
                    
                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

                    case 6:
                        return CreateActivity(parameters[0], parameters[1], parameters[2], parameters[3], parameters[4],
                            parameters[5]);

                    case 7:
                        return CreateActivity(parameters[0], parameters[1], parameters[2], parameters[3], parameters[4],
                            parameters[5], parameters[6]);

                    default:
                        throw this.ParametersCountException(3, 6, 7);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityDateTimeOffset CreateActivity(IWitParameter value)
        {
            if (!value.IsStringOrReference())
                throw this.ExpectedStringException(timeSpan => timeSpan.Value);

            return new WitActivityDateTimeOffset
            {
                Value = value,
            };
        }

        private WitActivityDateTimeOffset CreateActivity(IWitParameter year, IWitParameter month, IWitParameter day)
        {
            if (!year.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Year);

            if (!month.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Month);

            if (!day.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Day);

            return new WitActivityDateTimeOffset
            {
                Year = year,
                Month = month,
                Day = day
            };
        }

        private WitActivityDateTimeOffset CreateActivity(IWitParameter year, IWitParameter month, IWitParameter day, IWitParameter hour, IWitParameter minute, IWitParameter second)
        {
            if (!year.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Year);

            if (!month.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Month);

            if (!day.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Day);

            if (!hour.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Hour);

            if (!minute.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Minute);

            if (!second.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Second);

            return new WitActivityDateTimeOffset
            {
                Year = year,
                Month = month,
                Day = day,
                Hour = hour,
                Minute = minute,
                Second = second
            };
        }

        private WitActivityDateTimeOffset CreateActivity(IWitParameter year, IWitParameter month, IWitParameter day, IWitParameter hour, IWitParameter minute, IWitParameter second, IWitParameter offset)
        {
            if (!year.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Year);

            if (!month.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Month);

            if (!day.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Day);

            if (!hour.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Hour);

            if (!minute.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Minute);

            if (!second.IsNumericOrReference())
                throw this.ExpectedNumericException(dateTime => dateTime.Second);

            if (offset is not IWitReference offsetReference)
                throw this.ExpectedReferenceException(dateTime => dateTime.Offset);

            return new WitActivityDateTimeOffset
            {
                Year = year,
                Month = month,
                Day = day,
                Hour = hour,
                Minute = minute,
                Second = second,
                Offset = offsetReference
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
