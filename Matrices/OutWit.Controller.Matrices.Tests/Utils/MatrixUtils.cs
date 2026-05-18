using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;

namespace OutWit.Controller.Matrices.Tests.Utils;

public static class MatrixUtils
{
    public static WitMatrix<double> RandomMatrix(int rows, int columns, int seed = 0)
    {
        var random = new Random(seed);
        var data = new double[rows * columns];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = Math.Round(random.NextDouble() * 100, 2);
        }
        return WitMatrix<double>.Create(rows, columns, data);
    }
    
    public static WitMatrixSparse<double> RandomMatrixSparse(int rows, int columns, double sparsity = 0.9, int seed = 0)
    {
        var random = new Random(seed);
        var elements = new List<(int r, int c, double val)>();
        long totalCells = (long)rows * columns;
        int nonZeroCount = (int)(totalCells * (1.0 - sparsity));
        var occupiedIndices = new HashSet<long>();

        for (int i = 0; i < nonZeroCount; i++)
        {
            long index;
            int r, c;
            do
            {
                r = random.Next(0, rows);
                c = random.Next(0, columns);
                index = (long)r * columns + c;
            } while (!occupiedIndices.Add(index));
            
            elements.Add((r, c, Math.Round(random.NextDouble() * 100 + 1, 2)));
        }
        return WitMatrixSparse<double>.Create(rows, columns, elements);
    }

    public static WitMatrixSparse<double> RandomMatrixSparseBalanced(int rows, int cols, int avgNnzPerRow = 64, int heavyTailExtra = 0, int seed = 0)
    {
        var rnd = new Random(seed);
        var elements = new List<(int r, int c, double val)>(Math.Max(1, rows * avgNnzPerRow));

        for (int r = 0; r < rows; r++)
        {
            int d = avgNnzPerRow;
            if (heavyTailExtra > 0 && rnd.NextDouble() < 0.2)
                d += rnd.Next(1, heavyTailExtra + 1);

            if (d > cols) d = cols;

            var set = new HashSet<int>();
            while (set.Count < d) set.Add(rnd.Next(cols));
            var colsSorted = set.ToArray();
            Array.Sort(colsSorted);

            foreach (var c in colsSorted)
            {
                double v = 0.1 + 0.9 * rnd.NextDouble();
                elements.Add((r, c, v));
            }
        }

        return WitMatrixSparse<double>.Create(rows, cols, elements);
    }

    public static WitMatrix<T> ToDense<T>(this WitMatrixSparse<T> sparseMatrix) where T : struct, INumber<T>
    {
        var denseData = new T[sparseMatrix.RowCount * sparseMatrix.ColumnCount];
        for (int r = 0; r < sparseMatrix.RowCount; r++)
        {
            var row = sparseMatrix.GetRow(r);
            for (int c = 0; c < sparseMatrix.ColumnCount; c++)
            {
                denseData[r * sparseMatrix.ColumnCount + c] = row[c];
            }
        }
        return WitMatrix<T>.Create(sparseMatrix.RowCount, sparseMatrix.ColumnCount, denseData);
    }

    public static Matrix<double> ToMathNetMatrix(this IWitMatrix<double> me)
    {
        var rows = me.RowCount;
        var cols = me.ColumnCount;
        var matrix = Matrix<double>.Build.Dense(rows, cols);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                matrix[i, j] = me[i, j];
            }
        }

        return matrix;
    }
}