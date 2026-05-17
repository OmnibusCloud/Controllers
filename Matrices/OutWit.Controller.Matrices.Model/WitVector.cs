using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Controller.Matrices.Model.Interfaces;

namespace OutWit.Controller.Matrices.Model
{
    /// <summary>
    /// Represents a dense vector of a generic numeric type.
    /// </summary>
    [MemoryPackable]
    public partial class WitVector<T> : ModelBase, IWitVector<T> where T : INumber<T>
    {
        #region Fields

        private readonly T[] m_data;

        #endregion

        #region Constructors
        
        private WitVector(T[] data, VectorType type)
        {
            m_data = data;
            Type = type;
        }

        [MemoryPackConstructor]
        private WitVector(IReadOnlyList<T> data, VectorType type)
            : this(data.ToArray(), type)
        {
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"[{string.Join(", ", m_data)}]";
        }

        public IEnumerable<(int Index, T Value)> GetNonZeroElements()
        {
            for (int i = 0; i < m_data.Length; i++)
            {
                if (m_data[i] != T.Zero)
                    yield return (i, m_data[i]);
            }
        }

        public static WitVector<T> Create(IEnumerable<T> data, VectorType type = VectorType.Row)
        {
            return new WitVector<T>(data.ToArray(), type);
        }

        public static WitVector<T> Create(T[] data, VectorType type = VectorType.Row)
        {
            var dataCopy = new T[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            return new WitVector<T>(dataCopy, type);
        }
        
        public static WitVector<T> Create(int size, VectorType type = VectorType.Row)
        {
            return new WitVector<T>(new T[size], type);
        }

        #endregion

        #region Operators

        public static implicit operator WitVector<T>(T[] data)
        {
            return Create(data);
        }

        #endregion

        #region ModelBase

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitVector<T> other)
                return false;

            return Type.Is(other.Type) && 
                   m_data.Is(other.m_data);
        }
        
        public override WitVector<T> Clone()
        {
            return Create(m_data, Type);
        }

        #endregion
        
        #region IEnumerable

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)m_data).GetEnumerator();
        
        IEnumerator IEnumerable.GetEnumerator() => m_data.GetEnumerator();

        #endregion

        #region Properties
        
        public IReadOnlyList<T> Data => m_data;

        public VectorType Type { get; }
        
        [MemoryPackIgnore]
        public T this[int index]
        {
            get => m_data[index];
            set => m_data[index] = value;
        }

        public int Count => m_data.Length;

        #endregion
    }
}
