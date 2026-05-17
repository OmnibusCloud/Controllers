using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace OutWit.Controller.Matrices.Model.Interfaces;

/// <summary>
/// Defines a generic interface for a matrix.
/// </summary>
/// <typeparam name="T">The numeric type of the elements.</typeparam>
public interface IWitMatrix<T> : ICloneable
    where T : INumber<T>
{

    public void Save(string filePath);
    public Task SaveAsync(string filePath);
    public Task SaveAsync(Stream stream);
    
    IWitVector<T> GetRow(int rowIndex);
    IWitVector<T> GetColumn(int columnIndex);
    IWitMatrix<T> Transpose();
    
    int RowCount { get; }
    int ColumnCount { get; }
    
    T this[int row, int column] { get; set; }
}