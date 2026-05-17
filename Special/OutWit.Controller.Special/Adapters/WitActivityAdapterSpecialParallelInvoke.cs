using Microsoft.Extensions.Logging;
using OutWit.Common.Exceptions;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Threading.Tasks;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Status;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialParallelInvoke : WitActivityAdapterComposite<WitActivitySpecialParallelInvoke>, IWitActivityAdapter<WitActivitySpecialParallelInvoke>
    {
        #region Constructors

        public WitActivityAdapterSpecialParallelInvoke(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task ProcessInner(WitActivitySpecialParallelInvoke activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status, bool reportProgress)
        {
            if (activity.Activities.Count == 0)
                return;
            
            await Parallel.ForEachAsync(activity.Activities, ProcessingManager.CancellationToken(status.JobId),  async (childActivity, token) =>
            {
                if (status.IsFailed())
                    return;
                
                await ProcessingManager.WaitAsync(status.JobId);
                ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, childActivity, activityStatus, pool, reportProgress));
            });
        }

        #endregion

        #region Parsing
        
        protected override WitActivitySpecialParallelInvoke CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 0)
                    throw this.ParametersCountException(0);

                return new WitActivitySpecialParallelInvoke();
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
