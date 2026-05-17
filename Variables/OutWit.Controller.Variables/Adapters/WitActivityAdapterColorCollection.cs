using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Controller.Variables.Model;
using OutWit.Controller.Variables.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables.Adapters
{
    internal sealed class WitActivityAdapterColorCollection : WitActivityAdapterFunction<WitActivityColorCollection>, IWitActivityAdapter<WitActivityColorCollection>
    {
        #region Constructors

        public WitActivityAdapterColorCollection(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityColorCollection activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            IReadOnlyList<WitColor?>? collection;
            if (activity.Value is IWitReference)
            {
                if (!pool.TryGetCollection(activity.Value, out collection) || collection == null)
                    throw this.FailedToGetParameterValueException(activityObject => activityObject.Value);
            }
            else
            {
                if (!pool.TryGetCollection(activity.Value, out IReadOnlyList<object?>? value) || value == null)
                    throw this.FailedToGetParameterValueException(activityObject => activityObject.Value);
                
                collection = BuildCollection(pool, value);
            }
            
            bool result = pool.TrySetCollection(activity.ReturnReference, collection);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        private IReadOnlyList<WitColor?> BuildCollection(IWitVariablesCollection pool, IReadOnlyList<object?> value)
        {
            List<WitColor?> collection = new();
            foreach (var color in value)
            {
                switch (color)
                {
                    case string constant:
                        if (WitColor.TryParse(constant, out var parsedColor))
                            collection.Add(parsedColor);
                        else
                            throw this.FailedToGetParameterValueException(activityColor => activityColor.Value);
                        break;
                    case IWitReference reference:
                        if (pool.TryGetValue(reference, out WitColor? colorObject))
                            collection.Add(colorObject);
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

        protected override WitActivityColorCollection CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (parameters[0].IsArrayOrReference())
                    return new WitActivityColorCollection { Value = parameters[0] };
                
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
