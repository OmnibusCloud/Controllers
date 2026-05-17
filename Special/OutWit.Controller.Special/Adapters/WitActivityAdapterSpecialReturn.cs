using Microsoft.Extensions.Logging;
using OutWit.Common;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialReturn : WitActivityAdapterCommand<WitActivitySpecialReturn>, IWitActivityAdapter<WitActivitySpecialReturn>
    {
        #region Constructors
        public WitActivityAdapterSpecialReturn(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivitySpecialReturn activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if(activity.Values == null)
                return;
            
            var values = new List<object?>(activity.Values.Length);

            for (int i = 0; i < activity.Values.Length; i++)
            {
                if(!pool.TryGetObject(activity.Values[i], out object? value))
                    throw this.FailedToGetParameterValueException(activityReturn => activityReturn.Values![i]);
                
                values.Add(value);
            }

            ProcessingManager.Return(status.JobId, values.AsReadOnly());
        }

        #endregion

        #region Parising

        protected override WitActivitySpecialReturn CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                return new WitActivitySpecialReturn
                {
                    Values = parameters
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
