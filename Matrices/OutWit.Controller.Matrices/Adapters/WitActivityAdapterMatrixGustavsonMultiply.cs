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

        // Loads a benchmark dataset from the controller's module Resources/ dir.
        // OutWit.Engine.Assets stages these files during build:
        //   - Tier-2 NuGet consumer path: ResolveControllerAssetsTask fetches
        //     from the controller's GitHub Release and extracts into
        //     <consumer-output>/@Controllers/<Cfg>/matrices.module/Resources/.
        //   - In-solution ProjectReference path (tests): shared
        //     OutWit.Controller.targets stages the source Resources/ folder
        //     into @Controllers/<Cfg>/matrices.module/Resources/ at build time.
        //
        // The probe-walk handles both scenarios uniformly. Cannot use
        // typeof(...).Assembly.Location alone — under ProjectReference the
        // adapter DLL is loaded from the test's bin (where AssemblyLoadContext
        // serves the project copy), not from @Controllers/matrices.module/.
        // Same multi-root pattern Render's RenderBenchmarkHelper uses.
        private static byte[] LoadModuleResource(string filename)
        {
            foreach (var root in EnumerateModuleRoots())
            {
                var path = Path.Combine(root, RESOURCES_DIRECTORY_NAME, filename);
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
            }

            throw new FileNotFoundException(
                $"Matrices controller resource '{filename}' not found in any candidate " +
                $"module location. Searched: " +
                string.Join(", ", EnumerateModuleRoots()) +
                ". The file should have been staged into matrices.module/Resources/ " +
                "either by ResolveControllerAssetsTask (consumer build) or by the " +
                "shared OutWit.Controller.targets PostBuild (in-solution build).");
        }

        private static IEnumerable<string> EnumerateModuleRoots()
        {
            // The adapter's own assembly location — works in the normal product
            // case where the engine plugin loader loaded Matrices.dll from
            // @Controllers/<Cfg>/matrices.module/ directly.
            var assemblyDir = Path.GetDirectoryName(typeof(WitActivityAdapterMatrixGustavsonMultiply).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
                yield return assemblyDir;

            // Walk up from AppContext.BaseDirectory looking for a staged
            // @Controllers/<Cfg>/matrices.module/ folder. This covers the
            // in-solution ProjectReference test case where Assembly.Location
            // points at the test bin, not at the module folder.
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                yield return Path.Combine(dir, "@Controllers", "Debug", "matrices.module");
                yield return Path.Combine(dir, "@Controllers", "Release", "matrices.module");
                dir = Path.GetDirectoryName(dir);
            }
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
