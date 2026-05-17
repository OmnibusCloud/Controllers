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
    internal sealed class WitActivityAdapterMatrixGetColumns : WitActivityAdapterFunction<WitActivityMatrixGetColumns>, IWitActivityAdapter<WitActivityMatrixGetColumns>
    {
        #region Constructors

        public WitActivityAdapterMatrixGetColumns(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityMatrixGetColumns activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Matrix, out IWitMatrix<double>? matrix) || matrix == null)
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Matrix);

            IReadOnlyList<int>? indices = null;

            if (activity.Indices != null && (!pool.TryGetCollection(activity.Indices, out indices) || indices == null))
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Indices);

            if (indices == null)
            {
                var list = new List<int>(matrix.ColumnCount);
                for (int i = 0; i < matrix.ColumnCount; i++)
                    list.Add(i);
                indices = list;
            }

            IReadOnlyList<WitVector<double>> rows = indices.Select(index => matrix.GetColumn(index)).Cast<WitVector<double>>().ToArray();

            var result = pool.TrySetValue(activity.ReturnReference, rows);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Parsing

        protected override WitActivityMatrixGetColumns CreateActivity(IWitParameter[] parameters)
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
                        throw this.ParametersCountException(2);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }


        private WitActivityMatrixGetColumns CreateActivity(IWitParameter value)
        {
            if (value is not IWitReference reference)
                throw this.ExpectedReferenceException(matrix => matrix.Matrix);

            return new WitActivityMatrixGetColumns
            {
                Matrix = reference,
            };
        }
        
        private WitActivityMatrixGetColumns CreateActivity(IWitParameter value, IWitParameter indices)
        {
            if (value is not IWitReference reference)
                throw this.ExpectedReferenceException(matrix => matrix.Matrix);

            if (!indices.IsArrayOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Indices);

            return new WitActivityMatrixGetColumns
            {
                Matrix = reference,
                Indices = indices,
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
