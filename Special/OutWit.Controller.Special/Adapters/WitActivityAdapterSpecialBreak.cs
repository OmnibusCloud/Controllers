using System;
using System.Threading.Tasks;
using OutWit.Common;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialBreak : WitActivityAdapterCommand<WitActivitySpecialBreak>, IWitActivityAdapter<WitActivitySpecialBreak>
    {
        #region Constructors
        public WitActivityAdapterSpecialBreak(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivitySpecialBreak activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            activityStatus?.RequestBreak();
        }

        #endregion

        #region Parsing

        protected override WitActivitySpecialBreak CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 0)
                    throw this.ParametersCountException(0);

                return new WitActivitySpecialBreak();
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
