using System;
using System.Threading.Tasks;
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

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialDelayed : WitActivityAdapterComposite<WitActivitySpecialDelayed>, IWitActivityAdapter<WitActivitySpecialDelayed>
    {
        #region Constructors

        public WitActivityAdapterSpecialDelayed(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task ProcessInner(WitActivitySpecialDelayed activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status, bool reportProgress)
        {
            if (activity.Activities.Count == 0)
                return;

            if (!pool.TryGetValue(activity.Delay, out int delayMs))
                throw this.FailedToGetParameterValueException(delayed => delayed.Delay);
            
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ProcessingManager.CancellationToken(status.JobId));

            foreach (var childActivity in activity.Activities)
            {
                if (status.IsFailed())
                    return;

                await ProcessingManager.WaitAsync(status.JobId);
                ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, childActivity, activityStatus, pool, reportProgress));
            }

        }


        #endregion

        #region Parsing

        protected override WitActivitySpecialDelayed CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (!parameters.IsNumericOrReference() || parameters.Length != 1)
                    throw this.ExpectedNumericException(delayed => delayed.Delay);

                return new WitActivitySpecialDelayed
                {
                    Delay = parameters[0],
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
