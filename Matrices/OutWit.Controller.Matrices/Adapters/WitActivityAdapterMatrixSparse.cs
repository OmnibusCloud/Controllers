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
    internal sealed class WitActivityAdapterMatrixSparse : WitActivityAdapterFunction<WitActivityMatrixSparse>, IWitActivityAdapter<WitActivityMatrixSparse>
    {
        #region Constructors

        public WitActivityAdapterMatrixSparse(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivityMatrixSparse activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            WitMatrixSparse<double>? matrix = null;

            if (activity.Rows == null && activity.Columns == null)
                matrix = BuildFromData(pool, activity.Data);

            else if (!pool.TryGetValue(activity.Rows, out int rowCount))
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Rows);

            else if (!pool.TryGetValue(activity.Columns, out int columnCount))
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Columns);

            else if (!pool.TryGetCollection(activity.Data, out IReadOnlyList<object?[]?>? tuple) || tuple == null)
                throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
            else
                matrix = BuildFromVectors(rowCount, columnCount, tuple);

            var result = pool.TrySetValue(activity.ReturnReference, matrix);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        private WitMatrixSparse<double>? BuildFromData(IWitVariablesCollection pool, IWitParameter? data)
        {
            switch (data)
            {
                case IWitReference reference:
                    if (!pool.TryGetValue(reference, out WitMatrixSparse<double>? matrix))
                        throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
                    return matrix;

                default:
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
            }
        }

        private WitMatrixSparse<double> BuildFromVectors(int rowCount, int columnCount, IReadOnlyList<object?[]?> tuple)
        {
            var rows = new Dictionary<int, IReadOnlyList<(int Index, double Value)>>();
            int totalNonZeroCount = 0;
            int itemIndex = -1;
            foreach (object?[]? item in tuple)
            {
                itemIndex++;

                if(item == null)
                    continue;

                if (item.Length == 3)
                    return BuildFromElements(rowCount, columnCount, tuple);
                
                if(item.Length != 2)
                {
                    Logger.LogError("MatrixSparse Data tuple[{Index}] has invalid length {Length}. Expected 2 or 3.", itemIndex, item.Length);
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
                }
                
                if(item[0] is not int rowIndex)
                {
                    Logger.LogError("MatrixSparse Data tuple[{Index}][0] has invalid type {Type}.", itemIndex, item[0]?.GetType().FullName ?? "null");
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
                }

                IWitVector<double> row;

                if (item[1] is IWitVector<double> vectorRow)
                    row = vectorRow;
                else if (item[1] is IReadOnlyList<double> rowValues)
                    row = WitVector<double>.Create(rowValues, VectorType.Row);
                else
                {
                    Logger.LogError("MatrixSparse Data tuple[{Index}][1] has invalid type {Type}.", itemIndex, item[1]?.GetType().FullName ?? "null");
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);
                }

                IReadOnlyList<(int Index, double Value)> rowElements 
                    = row.GetNonZeroElements().OrderBy(x => x.Index).ToList();

                totalNonZeroCount += rowElements.Count;
                rows.TryAdd(rowIndex, rowElements);
            }

            var values = new double[totalNonZeroCount];
            var columnIndices = new int[totalNonZeroCount];
            var rowPointers = new int[rowCount + 1];

            int currentElementIndex = 0;
            int lastRowIndex = -1;

            foreach (var rowIndex in rows.Keys.OrderBy(r => r))
            {
                for (int i = lastRowIndex + 1; i < rowIndex; i++)
                    rowPointers[i] = currentElementIndex;

                rowPointers[rowIndex] = currentElementIndex;

                foreach (var (colIndex, value) in rows[rowIndex])
                {
                    values[currentElementIndex] = value;
                    columnIndices[currentElementIndex] = colIndex;
                    currentElementIndex++;
                }
                lastRowIndex = rowIndex;
            }

            for (int i = lastRowIndex + 1; i <= rowCount; i++)
                rowPointers[i] = currentElementIndex;

            return new WitMatrixSparse<double>(rowCount, columnCount, values, columnIndices, rowPointers);
        }

        public WitMatrixSparse<double> BuildFromElements(int rowCount, int columnCount, IReadOnlyList<object?[]?> tuple)
        {
            var elements = new List<(int Row, int Column, double Value)>();

            foreach (object?[]? item in tuple)
            {
                if (item == null)
                    continue;

                if (item.Length != 3)
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);

                if (item[0] is not int rowIndex)
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);

                if (item[1] is not int columnIndex)
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);

                if (item[2] is not double value)
                    throw this.FailedToGetParameterValueException(activityMatrix => activityMatrix.Data);

                elements.Add((rowIndex, columnIndex, value));
            }

            return WitMatrixSparse<double>.Create(rowCount, columnCount, elements);
        }

        #endregion

        #region Parsing

        protected override WitActivityMatrixSparse CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);

                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

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

        private WitActivityMatrixSparse CreateActivity(IWitParameter data)
        {
            if (data is not IWitReference reference)
                throw this.ExpectedReferenceException(matrix => matrix.Data);

            return new WitActivityMatrixSparse
            {
                Data = reference
            };
        }

        private WitActivityMatrixSparse CreateActivity(IWitParameter rows, IWitParameter columns, IWitParameter data)
        {
            if (!rows.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Rows);

            if (!columns.IsNumericOrReference())
                throw this.ExpectedNumericException(matrix => matrix.Columns);

            if (data is not IWitReference reference)
                throw this.ExpectedReferenceException(matrix => matrix.Data);

            return new WitActivityMatrixSparse
            {
                Rows = rows,
                Columns = columns,
                Data = reference,
            };
        }


        #endregion


        public IResources Resources { get; }

    }
}
