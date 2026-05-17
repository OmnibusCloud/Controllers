using System;
using System.Collections.Generic;
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
    internal sealed class WitActivityAdapterTupleCollection : WitActivityAdapterFunction<WitActivityTupleCollection>, IWitActivityAdapter<WitActivityTupleCollection>
    {
        #region Constructors

        public WitActivityAdapterTupleCollection(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityTupleCollection activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetCollection(activity.Value, out IReadOnlyList<object?[]?>? value) || value == null)
                throw this.FailedToGetParameterValueException(activityTuple => activityTuple.Value);
            
            bool result = pool.TrySetCollection(activity.ReturnReference, value);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityTupleCollection CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (parameters[0].IsArrayOrReference())
                    return new WitActivityTupleCollection { Value = parameters[0] };
                
                throw this.ExpectedReferenceException(activityTuple => activityTuple.Value);
                
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
