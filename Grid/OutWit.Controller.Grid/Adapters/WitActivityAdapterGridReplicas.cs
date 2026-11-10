using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Grid.Activities;
using OutWit.Controller.Grid.Interfaces;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Threading.Tasks;

namespace OutWit.Controller.Grid.Adapters
{
    internal sealed class WitActivityAdapterGridReplicas : WitActivityAdapterFunction<WitActivityGridReplicas>,
        IWitActivityAdapter<WitActivityGridReplicas>
    {
        #region Constants

        private const string ERROR_COPIES_RANGE = "Grid.Replicas: the number of executions must be from 1 to {0}, got {1}.";

        private const string ERROR_SHARE_RANGE = "Grid.Replicas: the share of the items must be from 0 to 1, got {0}.";

        #endregion

        #region Constructors

        public WitActivityAdapterGridReplicas(IWitProcessingManager processingManager, IResources resources, ILogger logger)
            : base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override Task Process(WitActivityGridReplicas activity, IWitVariablesCollection pool,
            IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Copies, out int copies))
                throw this.FailedToGetParameterValueException(replicas => replicas.Copies);

            double share = 1.0;

            if (activity.Share != null && !pool.TryGetValue(activity.Share, out share))
                throw this.FailedToGetParameterValueException(replicas => replicas.Share);

            if (copies < 1 || copies > WitGridReplicaPlanner.MAX_REPLICAS)
                throw new WitEngineActivityProcessingException<WitActivityGridReplicas>(
                    string.Format(ERROR_COPIES_RANGE, WitGridReplicaPlanner.MAX_REPLICAS, copies));

            if (!(share >= 0 && share <= 1))
                throw new WitEngineActivityProcessingException<WitActivityGridReplicas>(string.Format(ERROR_SHARE_RANGE, share));

            // A share of zero asks for nothing: the call must not count as one with copies,
            // which the server keeps on this cloud's own nodes.
            bool result = pool.TrySetValue(activity.ReturnReference, new WitProcessingOptions { Replicas = share > 0 ? copies : 1, ReplicaShare = share });

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

            return Task.CompletedTask;
        }

        #endregion

        #region Parsing

        protected override WitActivityGridReplicas CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0], null);

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

        private WitActivityGridReplicas CreateActivity(IWitParameter copies, IWitParameter? share)
        {
            if (!copies.IsNumericOrReference())
                throw this.ExpectedNumericException(replicas => replicas.Copies);

            if (share != null && !share.IsNumericOrReference())
                throw this.ExpectedNumericException(replicas => replicas.Share);

            return new WitActivityGridReplicas
            {
                Copies = copies,
                Share = share
            };
        }

        #endregion

        #region Properties

        public IResources Resources { get; }

        #endregion
    }
}
