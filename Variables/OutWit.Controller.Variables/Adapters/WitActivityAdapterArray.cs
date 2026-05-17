using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterArray : WitActivityAdapterFunction<WitActivityArray>, IWitActivityAdapter<WitActivityArray>
    {
        #region Constructors

        public WitActivityAdapterArray(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityArray activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            bool result;
            
            if(activity.Value is IWitArray array)
                result = pool.TrySetValue<IWitArray?>(activity.ReturnReference, array);
            else if (!pool.TryGetValue(activity.Value, out IWitArray? value))
                throw this.FailedToGetParameterValueException(activityArray => activityArray.Value);
            else 
                result = pool.TrySetValue<IWitArray?>(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityArray CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (!parameters[0].IsArrayOrReference())
                    throw this.ExpectedArrayException(activityArray => activityArray.Value);

                return new WitActivityArray
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
