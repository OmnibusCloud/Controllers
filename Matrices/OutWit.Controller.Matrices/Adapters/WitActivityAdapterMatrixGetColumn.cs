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
    internal sealed class WitActivityAdapterMatrixGetColumn : WitActivityAdapterFunction<WitActivityMatrixGetColumn>, IWitActivityAdapter<WitActivityMatrixGetColumn>
    {
        #region Constructors

        public WitActivityAdapterMatrixGetColumn(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityMatrixGetColumn activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Matrix, out IWitMatrix<double>? matrix) || matrix == null)
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Matrix);

            if (!pool.TryGetValue(activity.Index, out int index))
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Index);

            var result = pool.TrySetValue(activity.ReturnReference, matrix.GetColumn(index));

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Parsing

        protected override WitActivityMatrixGetColumn CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 2:
                        return CreateActivity(parameters[0], parameters[1]);

                    default:
                        throw this.ParametersCountException(2);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityMatrixGetColumn CreateActivity(IWitParameter value, IWitParameter index)
        {
            if (value is not IWitReference reference)
                throw this.ExpectedReferenceException(matrix => matrix.Matrix);

            if (!index.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Index);

            return new WitActivityMatrixGetColumn
            {
                Matrix = reference,
                Index = index,
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
