using Microsoft.Extensions.Logging;
using OutWit.Common;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialTimer : WitActivityAdapterComposite<WitActivitySpecialTimer>, IWitActivityAdapter<WitActivitySpecialTimer>
    {
        #region Constructors

        public WitActivityAdapterSpecialTimer(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task ProcessInner(WitActivitySpecialTimer activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status, bool reportProgress)
        {
            if (!pool.TryGetValue(activity.Interval, out int intervalMs))
                throw this.FailedToGetParameterValueException(timer => timer.Interval);
            
            TimeSpan interval = TimeSpan.FromMilliseconds(intervalMs);
            
            TimeSpan timeout = TimeSpan.MaxValue;
            if (pool.TryGetValue(activity.Timeout, out int timeoutMs))
                timeout = TimeSpan.FromMilliseconds(timeoutMs);

            TimeSpan totalTime = TimeSpan.Zero;
            while (totalTime < timeout)
            {
                if (status.IsFailed())
                    return;

                await ProcessingManager.WaitAsync(status.JobId);
                ProcessingManager.ThrowIfCancellationRequested(status.JobId);
                
                RunIteration(activity.Activities, pool, activityStatus, status);
                await Task.Delay(interval);

                totalTime = totalTime.Add(interval);
            }
        }

        private void RunIteration(IEnumerable<IWitActivity> activities, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            Task.Run(async () =>
            {
                foreach (var childActivity in activities)
                {
                    if (status.IsFailed())
                        return;
                    
                    await ProcessingManager.WaitAsync(status.JobId);
                    ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                    status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, childActivity, activityStatus, pool, false));
                }
            });
        }

        #endregion

        #region Parsing

        protected override WitActivitySpecialTimer CreateActivity(IWitParameter[] parameters)
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

        private WitActivitySpecialTimer CreateActivity(IWitParameter interval)
        {
            if (!interval.IsNumericOrReference())
                throw this.ExpectedNumericException(timer => timer.Timeout);

            return new WitActivitySpecialTimer
            {
                Interval = interval
            };
        }

        private WitActivitySpecialTimer CreateActivity(IWitParameter interval, IWitParameter timeout)
        {
            if (!interval.IsNumericOrReference())
                throw this.ExpectedStringException(timer => timer.Interval);

            if (!timeout.IsNumericOrReference())
                throw this.ExpectedNumericException(timer => timer.Timeout);

            return new WitActivitySpecialTimer
            {
                Interval = interval,
                Timeout = timeout
            };
        }

        #endregion

        public IResources Resources { get; }
    }
}
