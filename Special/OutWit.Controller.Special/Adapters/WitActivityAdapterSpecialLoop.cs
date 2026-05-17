using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    internal sealed class WitActivityAdapterSpecialLoop : WitActivityAdapterComposite<WitActivitySpecialLoop>, IWitActivityAdapter<WitActivitySpecialLoop>
    {
        #region Constructors

        public WitActivityAdapterSpecialLoop(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task ProcessInner(WitActivitySpecialLoop activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status, bool reportProgress)
        {
            if (activity.Activities.Count == 0)
                return;

            if (!pool.TryGetValue(activity.IterationsCount, out int iterationsCount) || iterationsCount <= 0)
                throw this.FailedToGetParameterValueException(loop => loop.IterationsCount);
            
            IWitActivityStatus? childStatus = activityStatus?.Child();
            for(int i = 0; i < iterationsCount; i++)
            {
                if (childStatus?.ShouldBreak == true)
                    return;
                
                if(childStatus?.ShouldContinue == true)
                    childStatus = activityStatus?.Child();
                
                foreach (var childActivity in activity.Activities)
                {
                    if (status.IsFailed())
                        return;
                    
                    if(childStatus?.ShouldBreak == true || childStatus?.ShouldContinue == true)
                        break;

                    await ProcessingManager.WaitAsync(status.JobId);
                    ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                    status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, childActivity, childStatus, pool, false));
                }
            }
        }

        #endregion

        #region Parsing

        protected override WitActivitySpecialLoop CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (!parameters.IsNumericOrReference() || parameters.Length != 1)
                    throw this.ExpectedNumericException(loop => loop.IterationsCount);

                return new WitActivitySpecialLoop
                {
                    IterationsCount = parameters[0]
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
