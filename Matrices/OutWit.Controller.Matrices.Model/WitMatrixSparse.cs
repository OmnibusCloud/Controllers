using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.MemoryPack;
using OutWit.Common.Values;
using OutWit.Controller.Matrices.Model.Interfaces;

namespace OutWit.Controller.Matrices.Model
{
    /// <summary>
    /// Represents a sparse matrix using the Compressed Sparse Row (CSR) format.
    /// This format is highly efficient for storage and row-based operations.
    /// Note: Modifying the structure (adding/removing non-zero elements) is computationally expensive.
    /// </summary>
    [MemoryPackable]
    public partial class WitMatrixSparse<T> : ModelBase, IWitMatrix<T> where T : INumber<T>
    {
        #region Fields

        // CSR format arrays
        private readonly T[] m_values; // Stores the non-zero values
        private readonly int[] m_columnIndices; // Stores the column index for each value
        private readonly int[] m_rowPointers; // Stores the start index of each row in the _values array

        #endregion

        #region Constructors

        public WitMatrixSparse(int rowCount, int columnCount, T[] values, int[] columnIndices, int[] rowPointers)
        {
            RowCount = rowCount;
            ColumnCount = columnCount;
            m_values = values;
            m_columnIndices = columnIndices;
            m_rowPointers = rowPointers;
        }

        [MemoryPackConstructor]
        private WitMatrixSparse(int rowCount, int columnCount, IReadOnlyList<T> values,
            IReadOnlyList<int> columnIndices, IReadOnlyList<int> rowPointers)
            : this(rowCount, columnCount, values.ToArray(), columnIndices.ToArray(), rowPointers.ToArray())
        {
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"Sparse Matrix ({RowCount}x{ColumnCount}), {m_values.Length} non-zero elements";
        }

        /// <summary>
        /// Creates a sparse matrix from a list of non-zero elements.
        /// </summary>
        /// <param name="rows">Total number of rows in the matrix.</param>
        /// <param name="columns">Total number of columns in the matrix.</param>
        /// <param name="nonZeroElements">A collection of tuples representing (rowIndex, columnIndex, value).</param>
        public static WitMatrixSparse<T> Create(int rows, int columns,
            IEnumerable<(int r, int c, T val)> nonZeroElements)
        {
            var elements = nonZeroElements
                .Where(e => e.val != T.Zero)
                .OrderBy(e => e.r)
                .ThenBy(e => e.c)
                .ToList();

            var values = new T[elements.Count];
            var columnIndices = new int[elements.Count];
            var rowPointers = new int[rows + 1];

            int currentElement = 0;
            int currentRow = 0;

            foreach (var (r, c, val) in elements)
            {
                while (currentRow < r)
                {
                    rowPointers[++currentRow] = currentElement;
                }

                values[currentElement] = val;
                columnIndices[currentElement] = c;
                currentElement++;
            }

            while (currentRow < rows)
            {
                rowPointers[++currentRow] = currentElement;
            }

            return new WitMatrixSparse<T>(rows, columns, values, columnIndices, rowPointers);
        }

        public static WitMatrixSparse<T>? Load(string filePath)
        {
            return File.ReadAllBytes(filePath).FromMemoryPackBytes<WitMatrixSparse<T>>();
        }
        
        public static async Task<WitMatrixSparse<T>?> LoadAsync(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            return await LoadAsync(stream);
        }

        public static async Task<WitMatrixSparse<T>?> LoadAsync(Stream stream)
        {
            return await MemoryPackSerializer.DeserializeAsync<WitMatrixSparse<T>>(stream);
        }

        #endregion

        #region IWitMatrix

        public void Save(string filePath)
        {
            File.WriteAllBytes(filePath, this.ToMemoryPackBytes());
        }

        public async Task SaveAsync(string filePath)
        {
            await using var stream = File.OpenWrite(filePath);
            await SaveAsync(stream);
        }
        
        public async Task SaveAsync(Stream stream)
        {
            await MemoryPackSerializer.SerializeAsync(stream, this);
        }

        public IWitVector<T> GetRow(int rowIndex)
        {
            int rowStart = m_rowPointers[rowIndex];
            int rowEnd = m_rowPointers[rowIndex + 1];
            int nnz = rowEnd - rowStart; // Number of non-zero elements in this row

            var rowValues = new T[nnz];
            var rowIndices = new int[nnz];

            Array.Copy(m_values, rowStart, rowValues, 0, nnz);
            Array.Copy(m_columnIndices, rowStart, rowIndices, 0, nnz);

            return new WitVectorSparse<T>(ColumnCount, rowValues, rowIndices, VectorType.Row);
        }

        public IWitVector<T> GetColumn(int columnIndex)
        {
            var colElements = new List<(int index, T value)>();
            for (int i = 0; i < RowCount; i++)
            {
                T val = this[i, columnIndex];
                if (val != T.Zero)
                {
                    colElements.Add((i, val));
                }
            }
            return WitVectorSparse<T>.Create(RowCount, colElements, VectorType.Column);
        }

        public IWitMatrix<T> Transpose()
        {
            // Transposing a CSR matrix is a non-trivial operation.
            // A common approach is to convert to Coordinate (COO) format first.
            var newElements = new List<(int r, int c, T val)>();
            for (int r = 0; r < RowCount; r++)
            {
                int rowStart = m_rowPointers[r];
                int rowEnd = m_rowPointers[r + 1];
                for (int i = rowStart; i < rowEnd; i++)
                {
                    newElements.Add((m_columnIndices[i], r, m_values[i]));
                }
            }

            // Create a new sparse matrix with swapped dimensions.
            return Create(ColumnCount, RowCount, newElements);
        }

        #endregion
        
        #region ModelBase

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitMatrixSparse<T> other)
                return false;

            return RowCount.Is(other.RowCount) &&
                   ColumnCount.Is(other.ColumnCount) &&
                   m_values.Is(other.m_values) &&
                   m_columnIndices.Is(other.m_columnIndices) &&
                   m_rowPointers.Is(other.m_rowPointers);
        }

        public override WitMatrixSparse<T> Clone()
        {
            return new WitMatrixSparse<T>(
                RowCount,
                ColumnCount,
                (T[])m_values.Clone(),
                (int[])m_columnIndices.Clone(),
                (int[])m_rowPointers.Clone()
            );
        }

        #endregion

        #region Properties

        public int RowCount { get; }
        public int ColumnCount { get; }
        
        public IReadOnlyList<T> Values => m_values;
        public IReadOnlyList<int> ColumnIndices => m_columnIndices;
        public IReadOnlyList<int> RowPointers => m_rowPointers;

        [MemoryPackIgnore]
        public T this[int row, int column]
        {
            get
            {
                int rowStart = m_rowPointers[row];
                int rowEnd = m_rowPointers[row + 1];

                // Binary search for the column index within the current row's segment
                int index = Array.BinarySearch(m_columnIndices, rowStart, rowEnd - rowStart, column);

                return index >= 0 ? m_values[index] : T.Zero;
            }
            set
            {
                throw new NotSupportedException(
                    "Setting individual elements in a SparseMatrix is not supported due to the CSR format. Recreate the matrix instead.");
            }
        }

        #endregion
    }
}