using System;
using System.Collections.Generic;
using System.Numerics;

namespace OutWit.Controller.Matrices.Model.Interfaces;

/// <summary>
/// Defines a generic interface for a vector.
/// </summary>
/// <typeparam name="T">The numeric type of the elements.</typeparam>
public interface IWitVector<T> : IReadOnlyList<T>, ICloneable 
    where T : INumber<T>
{
    /// <summary>
    /// Gets an enumerable of the non-zero elements, represented as (index, value) tuples.
    /// This is the primary way to iterate efficiently over sparse vectors.
    /// </summary>
    IEnumerable<(int Index, T Value)> GetNonZeroElements();

    VectorType Type { get; }
}

public enum VectorType
{
    Row,
    Column
}