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
using System;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Model.Math;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Exceptions;

namespace OutWit.Controller.Matrices.Adapters
{
    internal sealed class WitActivityAdapterMatrixGustavsonMultiply : WitActivityAdapterFunction<WitActivityMatrixGustavsonMultiply>, IWitActivityAdapter<WitActivityMatrixGustavsonMultiply>, IWitBenchmarkAdapter
    {
        #region Constants

        private const string UNIT = "gustavson-op@v1";

        // Benchmark dataset filenames. The matching ControllerDataAsset entries
        // in the csproj declare them as external assets; OutWit.Engine.Assets
        // stages them into <controller-module-dir>/Resources/ at build time.
        // Previously embedded as Properties.Resources but inflated the DLL to
        // ~50 MB — now loaded from disk on demand.
        private const string ROW_MATRIX_FILENAME = "rowMatrix.smat";
        private const string LARGE_MATRIX_FILENAME = "largeMatrix.smat";
        private const string RESOURCES_DIRECTORY_NAME = "Resources";

        #endregion

        #region Constructors

        public WitActivityAdapterMatrixGustavsonMultiply(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityMatrixGustavsonMultiply activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if (!pool.TryGetValue(activity.Matrix, out IWitMatrix<double>? matrix) || matrix == null)
                throw this.FailedToGetParameterValueException(multiply => multiply.Matrix);

            if (!pool.TryGetValue(activity.RowIndex, out int? rowIndex) || rowIndex == null)
                throw this.FailedToGetParameterValueException(multiply => multiply.RowIndex);

            IWitVector<double>? vector = null;
            
            if(pool.TryGetCollection(activity.RowVector, out IReadOnlyList<double>? values))
                vector = values != null ? WitVector<double>.Create(values) : null;
            else if(pool.TryGetValue(activity.RowVector, out IWitVector<double>? vectorValue))
                vector = vectorValue;
            else
                throw this.FailedToGetParameterValueException(multiply => multiply.RowVector);
            
            if(vector == null)
                throw this.FailedToGetParameterValueException(multiply => multiply.RowVector);

            if (rowIndex < 0 || rowIndex >= matrix.ColumnCount)
                throw new WitEngineActivityProcessingException<WitActivityMatrixGustavsonMultiply>($"Row index {rowIndex} is out of bounds.");

            var resultVector = SparseGustavson.MultiplyRowByMatrix(vector, matrix);

            // Use primitive row payload for cross-process safety:
            // IWitVector implementations may come from different plugin load contexts.
            var result = pool.TrySetValue(activity.ReturnReference, new object[] { rowIndex, resultVector.ToArray() });

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Benchmark

        public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
        {
            options ??= WitBenchmarkOptions.Default;

            (IWitVector<double> row, WitMatrixSparse<double> matrix) = GetBenchmarkData(options);
            
            long operationsPerCall = SparseGustavson.EstimateSparseGustavsonWork(row, matrix);

            for (int i = 0; i < options.WarmupIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SparseGustavson.MultiplyRowByMatrix(row, matrix);
            }

            long iterations = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                SparseGustavson.MultiplyRowByMatrix(row, matrix);
                iterations++;
            } while (stopwatch.Elapsed < options.MinDuration);
            
            stopwatch.Stop();
            
            double operationsPerSecond = iterations / stopwatch.Elapsed.TotalSeconds * operationsPerCall;

            return new WitBenchmarkResult
            {
                Rate = operationsPerSecond,
                Unit = UNIT,
                Elapsed = stopwatch.Elapsed,
                Iterations = iterations
            };
        }
        
        private (IWitVector<double>, WitMatrixSparse<double>) GetBenchmarkData(IWitBenchmarkOptions options)
        {
            IWitVector<double>? row = null;
            WitMatrixSparse<double>? matrix = null;

            if (!string.IsNullOrEmpty(options.DatasetPath) && Directory.Exists(options.DatasetPath))
            {
                var rowFilePath = Path.Combine(options.DatasetPath, ROW_MATRIX_FILENAME);
                if(File.Exists(rowFilePath))
                    row = File.ReadAllBytes(rowFilePath).FromMemoryPackBytes<WitMatrixSparse<double>>().GetRow(0);

                var matrixFilePath = Path.Combine(options.DatasetPath, LARGE_MATRIX_FILENAME);
                if (File.Exists(matrixFilePath))
                    matrix = File.ReadAllBytes(matrixFilePath).FromMemoryPackBytes<WitMatrixSparse<double>>();
            }

            row ??= LoadModuleResource(ROW_MATRIX_FILENAME).FromMemoryPackBytes<WitMatrixSparse<double>>().GetRow(0);
            matrix ??= LoadModuleResource(LARGE_MATRIX_FILENAME).FromMemoryPackBytes<WitMatrixSparse<double>>();

            return (row, matrix);
        }

        // Loads a benchmark dataset from the controller's module Resources/ dir
        // next to the assembly. OutWit.Engine.Assets stages these files during
        // build (asset resolver fetches them from the controller's GitHub Release
        // and extracts into <consumer-output>/@Controllers/<Cfg>/matrices.module/Resources/).
        private static byte[] LoadModuleResource(string filename)
        {
            var assemblyDir = Path.GetDirectoryName(typeof(WitActivityAdapterMatrixGustavsonMultiply).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDir))
                throw new InvalidOperationException(
                    "Cannot resolve the Matrices controller assembly location for module-resource loading.");

            var path = Path.Combine(assemblyDir, RESOURCES_DIRECTORY_NAME, filename);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Matrices controller resource '{filename}' not found at '{path}'. " +
                    "The file should have been staged by OutWit.Engine.Assets' " +
                    "ResolveControllerAssetsTask during consumer build.",
                    path);

            return File.ReadAllBytes(path);
        }

        protected override double EstimateWork(WitActivityMatrixGustavsonMultiply activity, IWitVariablesCollection pool)
        {
            if (!pool.TryGetValue(activity.Matrix, out WitMatrixSparse<double>? matrix) || matrix == null)
                throw this.FailedToGetParameterValueException(multiply => multiply.Matrix);

            IWitVector<double>? vector = null;

            if (pool.TryGetCollection(activity.RowVector, out IReadOnlyList<double>? values))
                vector = values != null ? WitVector<double>.Create(values) : null;
            else if (pool.TryGetValue(activity.RowVector, out IWitVector<double>? vectorValue))
                vector = vectorValue;
            else
                throw this.FailedToGetParameterValueException(multiply => multiply.RowVector);

            if (vector == null)
                throw this.FailedToGetParameterValueException(multiply => multiply.RowVector);
            
            return SparseGustavson.EstimateSparseGustavsonWork(vector, matrix);
        }

        #endregion

        #region Parsing

        protected override WitActivityMatrixGustavsonMultiply CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

                    default:
                        throw this.ParametersCountException(3);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityMatrixGustavsonMultiply CreateActivity(IWitParameter rowIndex, IWitParameter rowVector, IWitParameter matrix)
        {
            if (matrix is not IWitReference matrixReference)
                throw this.ExpectedReferenceException(activity => activity.Matrix);

            if (!rowIndex.IsNumericOrReference())
                throw this.ExpectedNumericException(activity => activity.RowIndex);

            if (!rowVector.IsArrayOrReference())
                throw this.ExpectedArrayException(activity => activity.RowVector);

            return new WitActivityMatrixGustavsonMultiply
            {
                Matrix = matrixReference,
                RowIndex = rowIndex,
                RowVector = rowVector
            };
        }

        #endregion


        public IResources Resources { get; }
    }
}
