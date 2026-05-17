using OutWit.Common.Abstract;
using OutWit.Controller.Matrices.Model.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MemoryPack;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Matrices.Model
{
    /// <summary>
    /// Represents a sparse vector, storing only non-zero elements and their indices.
    /// </summary>
    [MemoryPackable]
    public partial class WitVectorSparse<T> : ModelBase, IWitVector<T> 
        where T : INumber<T>
    {
        #region Fields

        private readonly T[] m_values;
        
        private readonly int[] m_indices;

        #endregion

        #region Constructors

        public WitVectorSparse(int count, T[] values, int[] indices, VectorType type)
        {
            Count = count;
            m_values = values;
            m_indices = indices;
            
            Type = type;
        }

        [MemoryPackConstructor]
        private WitVectorSparse(int count, IReadOnlyList<T> values, IReadOnlyList<int> indices, VectorType type)
            :this(count, values.ToArray(), indices.ToArray(), type)
        {
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            var valuesString = m_values.Length < 16 ?
                $"[{string.Join(", ", m_values)}]" :
                $"[{m_values.Length} elements]";

            var indicesString = m_indices.Length < 16
                ? $"[{string.Join(", ", m_indices)}]"
                : $"[{m_indices.Length} elements]";
            
            return $"{Count}, {valuesString}, {indicesString}, {Type}";
        }

        public static WitVectorSparse<T> Create(int dimension, IEnumerable<(int index, T value)> nonZeroElements, VectorType type = VectorType.Column)
        {
            var sortedElements = nonZeroElements.Where(e => e.value != T.Zero).OrderBy(e => e.index).ToList();
            var values = new T[sortedElements.Count];
            var indices = new int[sortedElements.Count];
            for (int i = 0; i < sortedElements.Count; i++)
            {
                values[i] = sortedElements[i].value;
                indices[i] = sortedElements[i].index;
            }
            return new WitVectorSparse<T>(dimension, values, indices, type);
        }

        #endregion

        #region ModelBase

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVectorSparse<T> other)
                return false;
            return Count.Is(other.Count) && 
                   Type.Is(other.Type) && 
                   Values.Is(other.Values) && 
                   Indices.Is(other.Indices);
        }

        public override WitVectorSparse<T> Clone()
        {
            return new WitVectorSparse<T>(Count, (T[])m_values.Clone(), (int[])m_indices.Clone(), Type);
        }

        #endregion

        #region IEnumerable

        public IEnumerable<(int Index, T Value)> GetNonZeroElements()
        {
            for (int i = 0; i < m_values.Length; i++)
            {
                yield return (m_indices[i], m_values[i]);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            int nonZeroIndex = 0;
            for (int i = 0; i < Count; i++)
            {
                if (nonZeroIndex < m_indices.Length && m_indices[nonZeroIndex] == i)
                {
                    yield return m_values[nonZeroIndex++];
                }
                else
                {
                    yield return T.Zero;
                }
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        #region Properties

        public int Count { get; }
        
        public IReadOnlyList<T> Values => m_values;
        
        public IReadOnlyList<int> Indices => m_indices;

        public VectorType Type { get; }

        [MemoryPackIgnore]
        public T this[int index]
        {
            get
            {
                if(index >= Count)
                    throw new IndexOutOfRangeException("Index is out of bounds.");

                int itemIndex = m_indices.AsSpan().BinarySearch(index);
                return itemIndex >= 0 ? m_values[itemIndex] : T.Zero;
            }
            set => throw new NotSupportedException("Individual elements of a sparse vector cannot be set. Recreate the vector instead.");
        }

        #endregion
    }
}
