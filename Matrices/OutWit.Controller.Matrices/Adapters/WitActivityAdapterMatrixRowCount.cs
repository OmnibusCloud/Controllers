using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Interfaces;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Controller.Matrices.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Adapters
{
    internal sealed class WitActivityAdapterMatrixRowCount : WitActivityAdapterFunction<WitActivityMatrixRowCount>, IWitActivityAdapter<WitActivityMatrixRowCount>
    {
        #region Constructors

        public WitActivityAdapterMatrixRowCount(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityMatrixRowCount activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Matrix, out IWitMatrix<double>? matrix) || matrix == null)
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Matrix);

            var result = pool.TrySetValue(activity.ReturnReference, matrix.RowCount);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Parsing

        protected override WitActivityMatrixRowCount CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);

                    default:
                        throw this.ParametersCountException(1);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityMatrixRowCount CreateActivity(IWitParameter value)
        {
            if (value is not IWitReference reference)
                throw this.ExpectedReferenceException(matrix => matrix.Matrix);

            return new WitActivityMatrixRowCount
            {
                Matrix = reference
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
