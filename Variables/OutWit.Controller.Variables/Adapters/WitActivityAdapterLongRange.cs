using System;
using System.Collections.Generic;
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
    internal sealed class WitActivityAdapterLongRange : WitActivityAdapterFunction<WitActivityLongRange>, IWitActivityAdapter<WitActivityLongRange>
    {
        #region Constructors

        public WitActivityAdapterLongRange(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityLongRange activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.From, out long from))
                throw this.FailedToGetParameterValueException(range => range.From);
            
            if (!pool.TryGetValue(activity.To, out long to))
                throw this.FailedToGetParameterValueException(range => range.To);
            
            if(to <= from)
                throw this.InvalidRangeException(activity.From, activity.To);

            if (!pool.TryGetValue(activity.Step, out long step))
                step = 1;

            var capacity = (int)Math.Ceiling((to - from) / (double)step);
            if(capacity < 1)
                throw this.InvalidRangeException(activity.From, activity.To, activity.Step);
            
            var values = new List<long>(capacity);
            for(long i = from; i < to; i += step)
                values.Add(i);
            
            bool result = pool.TrySetValue(activity.ReturnReference, values);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        } 

        #endregion

        #region Parsing

        protected override WitActivityLongRange CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 2:
                        return CreateActivity(parameters[0], parameters[1]);
                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

                    default:
                        throw this.ParametersCountException(2, 3);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);

            }
        }
        
        private WitActivityLongRange CreateActivity(IWitParameter from, IWitParameter to)
        {
            if (!from.IsNumericOrReference())
                throw this.ExpectedStringException(range => range.From);
            
            if (!to.IsNumericOrReference())
                throw this.ExpectedStringException(range => range.To);

            return new WitActivityLongRange
            {
                From = from,
                To = to,
            };
        }
        
        private WitActivityLongRange CreateActivity(IWitParameter from, IWitParameter to, IWitParameter step)
        {
            if (!from.IsNumericOrReference())
                throw this.ExpectedStringException(range => range.From);
            
            if (!to.IsNumericOrReference())
                throw this.ExpectedStringException(range => range.To);
            
            if (!step.IsNumericOrReference())
                throw this.ExpectedStringException(range => range.Step);

            return new WitActivityLongRange
            {
                From = from,
                To = to,
                Step = step
            };
        }

        #endregion

        public IResources Resources { get; }

    }
}
