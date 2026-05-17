using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    internal sealed class WitActivityAdapterDateTimeOffsetCollection : WitActivityAdapterFunction<WitActivityDateTimeOffsetCollection>, IWitActivityAdapter<WitActivityDateTimeOffsetCollection>
    {
        #region Constructors

        public WitActivityAdapterDateTimeOffsetCollection(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityDateTimeOffsetCollection activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            IReadOnlyList<DateTimeOffset?>? collection;
            if (activity.Value is IWitReference)
            {
                if (!pool.TryGetCollection(activity.Value, out collection) || collection == null)
                    throw this.FailedToGetParameterValueException(activityCollection => activityCollection.Value);
            }
            else
            {
                if (!pool.TryGetCollection(activity.Value, out IReadOnlyList<object?>? value) || value == null)
                    throw this.FailedToGetParameterValueException(activityCollection => activityCollection.Value);
                
                collection = BuildCollection(pool, value);
            }
            
            bool result = pool.TrySetCollection(activity.ReturnReference, collection);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 
        
        private IReadOnlyList<DateTimeOffset?> BuildCollection(IWitVariablesCollection pool, IReadOnlyList<object?> value)
        {
            List<DateTimeOffset?> collection = new();
            foreach (var color in value)
            {
                switch (color)
                {
                    case string constant:
                        if (DateTimeOffset.TryParse(constant, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDateTime))
                            collection.Add(parsedDateTime);
                        else
                            throw this.FailedToGetParameterValueException(activityColor => activityColor.Value);
                        break;
                    case IWitReference reference:
                        if (pool.TryGetValue(reference, out DateTimeOffset? dateTimeObject))
                            collection.Add(dateTimeObject);
                        else
                            throw this.FailedToGetParameterValueException(activityColor => activityColor.Value);
                        break;
                    
                    default:
                        throw this.FailedToGetParameterValueException(activityColor => activityColor.Value);
                }
            }
            return collection;
        }

        #endregion

        #region Parsing

        protected override WitActivityDateTimeOffsetCollection CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (parameters[0].IsArrayOrReference())
                    return new WitActivityDateTimeOffsetCollection { Value = parameters[0] };
                
                throw this.ExpectedReferenceException(activityObject => activityObject.Value);
                
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
