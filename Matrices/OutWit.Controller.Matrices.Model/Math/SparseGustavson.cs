using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OutWit.Controller.Matrices.Model.Interfaces;

namespace OutWit.Controller.Matrices.Model.Math
{
    public static class SparseGustavson
    {
        /// <summary>
        /// Calculates a single result row for sparse matrix multiplication (C[i,:] = rowA * matrixB).
        /// This is the core operation intended to be performed on each worker node.
        /// </summary>
        /// <param name="rowA">A sparse vector representing a single row from the first matrix (A).</param>
        /// <param name="matrixB">The entire second sparse matrix (B).</param>
        /// <returns>A new sparse vector representing the resulting row.</returns>
        public static IWitVector<T> MultiplyRowByMatrix<T>(IWitVector<T> rowA, IWitMatrix<T> matrixB)
            where T : INumber<T>
        {
            // The accumulator stores the running sum for each column index in the result row.
            // Using a dictionary is efficient for building a sparse result.
            var resultRowAccumulator = new Dictionary<int, T>();

            // Iterate through each non-zero element of the input row from matrix A.
            // `colIndexA` corresponds to the column index 'k' in the formula C[i,j] = sum(A[i,k] * B[k,j]).
            // `valueA` is the element A[i,k].
            foreach (var (colIndexA, valueA) in rowA.GetNonZeroElements())
            {
                // Get the corresponding row from matrix B. This is an efficient operation in CSR format.
                IWitVector<T> rowB = matrixB.GetRow(colIndexA);

                // Iterate through each non-zero element of the fetched row from matrix B.
                // `colIndexB` corresponds to the column index 'j'.
                // `valueB` is the element B[k,j].
                foreach (var (colIndexB, valueB) in rowB.GetNonZeroElements())
                {
                    var product = valueA * valueB;

                    // Accumulate the product in the result row at the correct column index.
                    if (resultRowAccumulator.TryGetValue(colIndexB, out var currentValue))
                    {
                        resultRowAccumulator[colIndexB] = currentValue + product;
                    }
                    else
                    {
                        resultRowAccumulator[colIndexB] = product;
                    }
                }
            }

            // Convert the accumulator dictionary into a final sparse vector.
            // The Create method will handle sorting by index and filtering out any resultant zeros.
            var resultElements = resultRowAccumulator.Select(pair => (pair.Key, pair.Value));

            return WitVectorSparse<T>.Create(matrixB.ColumnCount, resultElements, VectorType.Row);
        }

        /// <summary>
        /// Estimates the number of scalar multiplication operations required to multiply a sparse row by a sparse matrix.
        /// This is the core of Gustavson's algorithm cost estimation.
        /// </summary>
        /// <returns>The total estimated number of scalar multiplication operations (workload).</returns>
        public static long EstimateSparseGustavsonWork<T>(IWitVector<T> rowA, WitMatrixSparse<T> matrixB)
            where T : INumber<T>
        {
            int[] nonZeroCountsPerRowB = GetRowNonZeroCounts(matrixB);
            long totalOperations = 0;

            foreach (var (colIndexA, _) in rowA.GetNonZeroElements())
            {
                if (colIndexA < nonZeroCountsPerRowB.Length)
                    totalOperations += nonZeroCountsPerRowB[colIndexA];
            }

            return totalOperations;
        }

        /// <summary>
        /// Pre-computes an array containing the number of non-zero elements for each row of a matrix.
        /// This is a preparatory step that should be performed once before estimating the work for multiple rows.
        /// </summary>
        /// <returns>An array where the value at index 'k' is the count of non-zero elements in row 'k' of the matrix.</returns>
        public static int[] GetRowNonZeroCounts<T>(WitMatrixSparse<T> matrix)
            where T : INumber<T>
        {
            int rowCount = matrix.RowCount;
            var nonZeroCounts = new int[rowCount];
            
            var rowPointers = matrix.RowPointers;
            for (int i = 0; i < rowCount; i++)
            {
                nonZeroCounts[i] = rowPointers[i + 1] - rowPointers[i];
            }

            return nonZeroCounts;
        }
    }
}
