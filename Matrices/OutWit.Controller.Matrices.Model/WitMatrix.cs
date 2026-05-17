using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
    /// Represents a dense matrix, storing elements in a contiguous 1D array in row-major order for performance.
    /// </summary>
    [MemoryPackable]
    public partial class WitMatrix<T> : ModelBase, IWitMatrix<T> where T : INumber<T>
    {
        #region Fields

        private readonly T[] m_data;

        #endregion

        #region Constructors

        private WitMatrix(int rowCount, int columnCount, T[] data)
        {
            if (data.Length != rowCount * columnCount)
                throw new ArgumentException("Data array length must match rows * columns.");

            RowCount = rowCount;
            ColumnCount = columnCount;
            m_data = data;
        }

        [MemoryPackConstructor]
        private WitMatrix(int rowCount, int columnCount, IReadOnlyList<T> data)
            : this(rowCount, columnCount, data.ToArray())
        {
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < RowCount; i++)
            {
                sb.Append("[ ");
                for (int j = 0; j < ColumnCount; j++)
                {
                    sb.Append($"{this[i, j]}");
                    if (j < ColumnCount - 1) sb.Append(", ");
                }

                sb.Append(" ]");
            }

            return $"[{sb}]";
        }

        /// <summary>
        /// Creates a new matrix, making a defensive copy of the provided data.
        /// </summary>
        public static WitMatrix<T> Create(int rows, int columns, T[] data)
        {
            var dataCopy = new T[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            return new WitMatrix<T>(rows, columns, dataCopy);
        }

        public static WitMatrix<T> Create(int rows, int columns, Span<T> data)
        {
            return new WitMatrix<T>(rows, columns, data.ToArray());
        }

        public static WitMatrix<T> Create(int rows, int columns, IReadOnlyList<T> data)
        {
            return new WitMatrix<T>(rows, columns, data.ToArray());
        }

        /// <summary>
        /// Creates a new matrix initialized with default values (zeroes for numeric types).
        /// </summary>
        public static WitMatrix<T> Create(int rows, int columns)
        {
            return new WitMatrix<T>(rows, columns, new T[rows * columns]);
        }

        /// <summary>
        /// Creates an identity matrix of the specified size.
        /// </summary>
        public static WitMatrix<T> CreateIdentity(int size)
        {
            var matrix = Create(size, size);
            for (int i = 0; i < size; i++)
            {
                matrix[i, i] = T.One;
            }

            return matrix;
        }
        
        public static WitMatrix<T>? Load(string filePath)
        {
            return File.ReadAllBytes(filePath).FromMemoryPackBytes<WitMatrix<T>>();
        }
        
        public static async Task<WitMatrix<T>?> LoadAsync(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            return await LoadAsync(stream);
        }

        public static async Task<WitMatrix<T>?> LoadAsync(Stream stream)
        {
            return await MemoryPackSerializer.DeserializeAsync<WitMatrix<T>>(stream);
        }

        #endregion

        #region Operators

        public static implicit operator WitMatrix<T>(T[,] data)
        {
            int rows = data.GetLength(0);
            int cols = data.GetLength(1);
            
            if (rows == 0 || cols == 0) 
                return Create(rows, cols);

            return Create(rows, cols, MemoryMarshal.CreateSpan(ref data[0, 0], data.Length));
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
            var rowData = new T[ColumnCount];
            Array.Copy(m_data, rowIndex * ColumnCount, rowData, 0, ColumnCount);
            return WitVector<T>.Create(rowData, VectorType.Row);
        }

        public IWitVector<T> GetColumn(int columnIndex)
        {
            var colData = new T[RowCount];
            for (int i = 0; i < RowCount; i++)
            {
                colData[i] = this[i, columnIndex];
            }

            return WitVector<T>.Create(colData, VectorType.Column);
        }

        public IWitMatrix<T> Transpose()
        {
            WitMatrix<T> result = Create(ColumnCount, RowCount);
            for (int i = 0; i < RowCount; i++)
            {
                for (int j = 0; j < ColumnCount; j++)
                {
                    result[j, i] = this[i, j];
                }
            }

            return result;
        }

        #endregion

        #region ModelBase

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitMatrix<T> other)
                return false;

            return RowCount.Is(other.RowCount) &&
                   ColumnCount.Is(other.ColumnCount) &&
                   m_data.Is(other.m_data);
        }

        public override WitMatrix<T> Clone()
        {
            return Create(RowCount, ColumnCount, m_data);
        }

        #endregion

        #region Properties

        public int RowCount { get; }
        public int ColumnCount { get; }

        public IReadOnlyList<T> Data => m_data;

        [MemoryPackIgnore]
        public T this[int row, int column]
        {
            get => m_data[row * ColumnCount + column];
            set => m_data[row * ColumnCount + column] = value;
        }

        #endregion
    }
}