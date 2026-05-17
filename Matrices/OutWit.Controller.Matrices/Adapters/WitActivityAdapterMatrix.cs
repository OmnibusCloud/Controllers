using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Interfaces;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Controller.Matrices.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System.Reflection.Metadata;

namespace OutWit.Controller.Matrices.Adapters
{
    internal sealed class WitActivityAdapterMatrix : WitActivityAdapterFunction<WitActivityMatrix>, IWitActivityAdapter<WitActivityMatrix>
    {
        #region Constructors

        public WitActivityAdapterMatrix(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityMatrix activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            WitMatrix<double>? matrix = null;

            if (activity.Rows == null && activity.Columns == null)
                matrix = BuildFromData(pool, activity.Data);

            else if (!pool.TryGetValue(activity.Rows, out int rows))
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Rows);

            else if (!pool.TryGetValue(activity.Columns, out int columns))
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Columns);
            
            else if(activity.Data == null)
                matrix = WitMatrix<double>.Create(rows, columns);

            else if(!pool.TryGetCollection(activity.Data, out IReadOnlyList<double>? values) || values == null)
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
            else
                matrix = WitMatrix<double>.Create(rows, columns, values);

            var result = pool.TrySetValue(activity.ReturnReference, matrix);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        private WitMatrix<double>? BuildFromData(IWitVariablesCollection pool, IWitParameter? data)
        {
            switch (data)
            {
                case IWitReference reference:
                    if (pool.TryGetValue(reference, out WitMatrix<double>? matrix))
                        return matrix;
                    if (pool.TryGetCollection(reference.Reference, out IReadOnlyList<WitVector<double>?>? vectors) && vectors != null)
                        return BuildFromVectors(vectors);
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);

                case IWitArray array:
                    if (!pool.TryGetCollection(array, out IReadOnlyList<IWitArray?>? arrays) || arrays == null || arrays.Count == 0)
                        throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
                    return BuildFromArray(pool, arrays);

                default:
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
            }
        }

        private WitMatrix<double> BuildFromArray(IWitVariablesCollection pool, IReadOnlyList<IWitArray?>? arrays)
        {
            var rows = new List<double>();

            int rowsCount = arrays.Count;
            int columnsCount = arrays[0]?.Count ?? 0;
            foreach (var array in arrays)
            {
                if (array == null)
                    throw this.FailedToGetParameterValueException(activityVector => activityVector.Data);

                if (!pool.TryGetCollection(array, out IReadOnlyList<double>? values) || values == null)
                    throw this.FailedToGetParameterValueException(activityVector => activityVector.Data);

                rows.AddRange(values);
            }

            return WitMatrix<double>.Create(rowsCount, columnsCount, rows);
        }

        private WitMatrix<double> BuildFromVectors(IReadOnlyList<WitVector<double>?> vectors)
        {
            var rows = new List<double>();

            int rowsCount = vectors.Count;
            int columnsCount = vectors[0]?.Count ?? 0;
            foreach (var vector in vectors)
            {
                if (vector == null)
                    throw this.FailedToGetParameterValueException(activityVector => activityVector.Data);

                rows.AddRange(vector);
            }

            return WitMatrix<double>.Create(rowsCount, columnsCount, rows);
        }

        #endregion

        #region Parsing

        protected override WitActivityMatrix CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);
                    
                    case 2:
                        return CreateActivity(parameters[0], parameters[1]);

                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

                    default:
                        throw this.ParametersCountException(1, 2, 3);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityMatrix CreateActivity(IWitParameter data)
        {
            if (!data.IsArrayOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Data);

            return new WitActivityMatrix
            {
                Data = data
            };
        }

        private WitActivityMatrix CreateActivity(IWitParameter rows, IWitParameter columns)
        {
            if (!rows.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Rows);

            if (!columns.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Columns);

            return new WitActivityMatrix
            {
                Rows = rows,
                Columns = columns,
            };
        }

        private WitActivityMatrix CreateActivity(IWitParameter rows, IWitParameter columns, IWitParameter data)
        {
            if (!rows.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Rows);

            if (!columns.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Columns);

            if (!data.IsArrayOrReference())
                throw this.ExpectedArrayException(matrix => matrix.Data);

            return new WitActivityMatrix
            {
                Rows = rows,
                Columns = columns,
                Data = data
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
