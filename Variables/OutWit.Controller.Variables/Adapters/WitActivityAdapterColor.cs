using Microsoft.Extensions.Logging;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Xml.Linq;
using OutWit.Controller.Variables.Model;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Status;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterColor : WitActivityAdapterFunction<WitActivityColor>, IWitActivityAdapter<WitActivityColor>
    {
        #region Constructors

        public WitActivityAdapterColor(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityColor activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (activity.Value != null)
                await ProcessFromValue(activity, pool, activityStatus, status);

            else
                await ProcessFromParameters(activity, pool, activityStatus, status);
        }
        
        protected async Task ProcessFromValue(WitActivityColor activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result;

            if (activity.Value is WitConstantString constant && WitColor.TryParse(constant.GetValue<string>(), out WitColor? color))
                result = pool.TrySetValue<WitColor?>(activity.ReturnReference, color);
            else if (!pool.TryGetValue(activity.Value, out WitColor? value))
                throw this.FailedToGetParameterValueException(activityColor => activityColor.Value);
            else
                result = pool.TrySetValue<WitColor?>(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        }

        protected async Task ProcessFromParameters(WitActivityColor activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Red, out byte red))
                throw this.FailedToGetParameterValueException(color => color.Red);

            if (!pool.TryGetValue(activity.Green, out byte green))
                throw this.FailedToGetParameterValueException(color => color.Green);

            if (!pool.TryGetValue(activity.Blue, out byte blue))
                throw this.FailedToGetParameterValueException(color => color.Blue);

            bool result = pool.TrySetValue(activity.ReturnReference, pool.TryGetValue(activity.Alpha, out byte alpha)
                ? new WitColor(red, green, blue, alpha)
                : new WitColor(red, green, blue));

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Parsing

        protected override WitActivityColor CreateActivity(IWitParameter[] parameters)
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

        private WitActivityColor CreateActivity(IWitParameter value)
        {
            if (!value.IsStringOrReference())
                throw this.ExpectedNumericException(color => color.Value);

            return new WitActivityColor
            {
                Value = value
            };
        }

        private WitActivityColor CreateActivity(IWitParameter red, IWitParameter green, IWitParameter blue)
        {
            if (!red.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Red);

            if (!green.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Green);

            if (!blue.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Blue);

            return new WitActivityColor
            {
                Red = red,
                Green = green,
                Blue = blue
            };
        }

        private WitActivityColor CreateActivity(IWitParameter red, IWitParameter green, IWitParameter blue, IWitParameter alpha)
        {
            if (!red.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Red);

            if (!green.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Green);

            if (!blue.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Blue);

            if (!alpha.IsNumericOrReference())
                throw this.ExpectedNumericException(color => color.Alpha); ;

            return new WitActivityColor
            {
                Red = red,
                Green = green,
                Blue = blue,
                Alpha = alpha
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
