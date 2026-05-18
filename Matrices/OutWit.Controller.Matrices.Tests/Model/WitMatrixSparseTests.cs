using System.Diagnostics;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Tests.Utils;

namespace OutWit.Controller.Matrices.Tests.Model;

[TestFixture]
public class WitMatrixSparseTests
{
    [Test]
    public void CreateTest()
    {
        var elements = new List<(int r, int c, int val)>
        {
            (0, 1, 10),
            (0, 3, 20),
            (2, 2, 30),
            (3, 0, 40),
            (3, 4, 50)
        };

        var matrix = WitMatrixSparse<int>.Create(4, 5, elements);

        Assert.That(matrix.RowCount, Is.EqualTo(4));
        Assert.That(matrix.ColumnCount, Is.EqualTo(5));
        Assert.That(matrix.Values, Is.EqualTo(new[] { 10, 20, 30, 40, 50 }));
        Assert.That(matrix.ColumnIndices, Is.EqualTo(new[] { 1, 3, 2, 0, 4 }));
        Assert.That(matrix.RowPointers, Is.EqualTo(new[] { 0, 2, 2, 3, 5 }));
    }

    [Test]
    public void CreateWithEmptyRowTest()
    {
        var elements = new List<(int r, int c, int val)>
        {
            (0, 0, 5),
            (2, 1, 8)
        };

        var matrix = WitMatrixSparse<int>.Create(3, 2, elements);

        Assert.That(matrix.RowCount, Is.EqualTo(3));
        Assert.That(matrix.RowPointers, Is.EqualTo(new[] { 0, 1, 1, 2 }));
        Assert.That(matrix[1, 0], Is.EqualTo(0));
        Assert.That(matrix[1, 1], Is.EqualTo(0));
    }
    
    [Test]
    public void CreateHandlesUnsortedAndZeroValueElementsTest()
    {
        var elements = new List<(int r, int c, int val)>
        {
            (1, 1, 20),
            (0, 2, 10),
            (1, 0, 0),
            (0, 0, 5)
        };

        var matrix = WitMatrixSparse<int>.Create(2, 3, elements);

        Assert.That(matrix.RowCount, Is.EqualTo(2));
        Assert.That(matrix.ColumnCount, Is.EqualTo(3));
        Assert.That(matrix.Values, Is.EqualTo(new[] { 5, 10, 20 }));
        Assert.That(matrix.ColumnIndices, Is.EqualTo(new[] { 0, 2, 1 }));
        Assert.That(matrix.RowPointers, Is.EqualTo(new[] { 0, 2, 3 }));
        Assert.That(matrix[1, 0], Is.EqualTo(0));
    }
    
    [Test]
    public void CreateEmptyMatrixTest()
    {
        var elements = new List<(int r, int c, int val)>();

        var matrix = WitMatrixSparse<int>.Create(3, 3, elements);

        Assert.That(matrix.RowCount, Is.EqualTo(3));
        Assert.That(matrix.ColumnCount, Is.EqualTo(3));
        Assert.That(matrix.Values, Is.Empty);
        Assert.That(matrix.ColumnIndices, Is.Empty);
        Assert.That(matrix.RowPointers, Is.EqualTo(new[] { 0, 0, 0, 0 }));
        Assert.That(matrix[1, 1], Is.EqualTo(0));
    }

    [Test]
    public void IndexerGetTest()
    {
        var elements = new List<(int r, int c, int val)>
        {
            (0, 1, 10), (2, 2, 30), (3, 0, 40)
        };
        var matrix = WitMatrixSparse<int>.Create(4, 4, elements);

        Assert.That(matrix[0, 1], Is.EqualTo(10));
        Assert.That(matrix[2, 2], Is.EqualTo(30));
        Assert.That(matrix[3, 0], Is.EqualTo(40));
        Assert.That(matrix[0, 0], Is.EqualTo(0));
        Assert.That(matrix[1, 1], Is.EqualTo(0));
    }

    [Test]
    public void IndexerSetThrowsExceptionTest()
    {
        var matrix = WitMatrixSparse<int>.Create(2, 2, new List<(int, int, int)>());

        Assert.Throws<NotSupportedException>(() => { matrix[0, 0] = 1; });
    }

    [Test]
    public void IsTest()
    {
        var elements = new List<(int r, int c, int val)> { (0, 1, 10), (1, 0, 20) };
        var matrixA = WitMatrixSparse<int>.Create(2, 2, elements);
        var matrixB = WitMatrixSparse<int>.Create(2, 2, elements);
        var matrixC = WitMatrixSparse<int>.Create(2, 2, new List<(int, int, int)> { (0, 1, 10), (1, 0, 99) }); // Different values
        var matrixD = WitMatrixSparse<int>.Create(3, 2, elements); // Different row count
        var matrixE = WitMatrixSparse<int>.Create(2, 3, elements); // Different col count
        
        Assert.That(matrixA, Was.EqualTo(matrixB));
        Assert.That(matrixA, Was.Not.EqualTo(matrixC));
        Assert.That(matrixA, Was.Not.EqualTo(matrixD));
        Assert.That(matrixA, Was.Not.EqualTo(matrixE));
        Assert.That(matrixA, Was.Not.EqualTo(null));
    }
    
    [Test]
    public void CloneTest()
    {
        var elements = new List<(int r, int c, int val)> { (0, 1, 10), (1, 0, 20) };
        var original = WitMatrixSparse<int>.Create(2, 2, elements);
        WitMatrixSparse<int> clone = original.Clone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.RowCount, Is.EqualTo(original.RowCount));
        Assert.That(clone.ColumnCount, Is.EqualTo(original.ColumnCount));
        Assert.That(clone.ColumnIndices, Is.EqualTo(original.ColumnIndices));
        Assert.That(clone.RowPointers, Is.EqualTo(original.RowPointers));
        Assert.That(clone.Values, Is.EqualTo(original.Values));
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var elements = new List<(int r, int c, int val)> { (0, 1, 10), (1, 0, 20) };
        var original = WitMatrixSparse<int>.Create(2, 2, elements);
        WitMatrixSparse<int> clone = original.MemoryPackClone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.RowCount, Is.EqualTo(original.RowCount));
        Assert.That(clone.ColumnCount, Is.EqualTo(original.ColumnCount));
        Assert.That(clone.ColumnIndices, Is.EqualTo(original.ColumnIndices));
        Assert.That(clone.RowPointers, Is.EqualTo(original.RowPointers));
        Assert.That(clone.Values, Is.EqualTo(original.Values));
    }
    
    [Test]
    public void GetRowTest()
    {
        var elements = new List<(int r, int c, int val)> { (0, 1, 10), (0, 3, 20), (2, 2, 30) };
        var matrix = WitMatrixSparse<int>.Create(3, 4, elements);

        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);

        Assert.That(row0.ToArray(), Is.EqualTo(new[] { 0, 10, 0, 20 }));
        Assert.That(row1.ToArray(), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        Assert.That(row2.ToArray(), Is.EqualTo(new[] { 0, 0, 30, 0 }));
    }

    [Test]
    public void GetColumnTest()
    {
        var elements = new List<(int r, int c, int val)> { (0, 1, 10), (2, 1, 30), (3, 0, 40) };
        var matrix = WitMatrixSparse<int>.Create(4, 2, elements);

        var col0 = matrix.GetColumn(0);
        var col1 = matrix.GetColumn(1);

        Assert.That(col0.ToArray(), Is.EqualTo(new[] { 0, 0, 0, 40 }));
        Assert.That(col1.ToArray(), Is.EqualTo(new[] { 10, 0, 30, 0 }));
    }

    [Test]
    public void TransposeTest()
    {
        var elements = new List<(int r, int c, int val)> { (0, 2, 10), (1, 0, 20), (1, 2, 30) };
        var matrix = WitMatrixSparse<int>.Create(2, 3, elements);
        
        // Original:
        // [ 0, 0, 10 ]
        // [ 20, 0, 30 ]

        var transposed = (WitMatrixSparse<int>)matrix.Transpose();

        // Expected Transposed:
        // [ 0, 20 ]
        // [ 0, 0  ]
        // [ 10, 30]

        Assert.That(transposed.RowCount, Is.EqualTo(3));
        Assert.That(transposed.ColumnCount, Is.EqualTo(2));
        Assert.That(transposed[0, 1], Is.EqualTo(20));
        Assert.That(transposed[2, 0], Is.EqualTo(10));
        Assert.That(transposed[2, 1], Is.EqualTo(30));
        Assert.That(transposed[1, 1], Is.EqualTo(0));
    }

    [Test]
    public async Task SaveLoadAsyncTest()
    {
        // Arrange
        var elements = new List<(int r, int c, double val)> { (0, 1, 1.1), (1, 0, 2.2) };
        var originalMatrix = WitMatrixSparse<double>.Create(2, 2, elements);
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.smat");

        try
        {
            // Act
            await originalMatrix.SaveAsync(filePath);
            var loadedMatrix = await WitMatrixSparse<double>.LoadAsync(filePath);

            // Assert
            Assert.That(loadedMatrix, Is.Not.Null);
            Assert.That(originalMatrix.Is(loadedMatrix), Is.True);
            Assert.That(loadedMatrix[0, 1], Is.EqualTo(1.1));
            Assert.That(loadedMatrix[1, 1], Is.EqualTo(0));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
    
    [Test]
    public void RandomSparseMatrixSaveLoadTest()
    {
        var originalMatrix = MatrixUtils.RandomMatrixSparse(20, 20, 0.8, 456);
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.smat");

        try
        {
            originalMatrix.Save(filePath);
            var loadedMatrix = WitMatrixSparse<double>.Load(filePath);

            Assert.That(loadedMatrix, Is.Not.Null);
            Assert.That(originalMatrix.Is(loadedMatrix), Is.True);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    [Explicit]
    public void LoadingTest()
    {
        Stopwatch timer = new Stopwatch();
        timer.Start();

        var matrix = MatrixUtils.RandomMatrixSparse(10000, 10000);
        
        timer.Stop();
        
        Console.WriteLine($"Matrix creation took: {timer.ElapsedMilliseconds} ms");
        
        timer.Reset();
        timer.Start();

        var bytes = matrix.ToMemoryPackBytes();
        
        timer.Stop();
        Console.WriteLine($"Serialization took: {timer.ElapsedMilliseconds} ms");
        
        timer.Reset();
        timer.Start();

        var matrix1 = bytes.FromMemoryPackBytes<WitMatrixSparse<double>>();
        
        timer.Stop();
        Console.WriteLine($"Deserialization took: {timer.ElapsedMilliseconds} ms");
        
    }

    [Test]
    [Explicit]
    public void LoadingBalancedTest()
    {
        Stopwatch timer = new Stopwatch();
        timer.Start();

        var matrix = MatrixUtils.RandomMatrixSparseBalanced(65536, 65536);

        timer.Stop();

        Console.WriteLine($"Matrix creation took: {timer.ElapsedMilliseconds} ms");

        timer.Reset();
        timer.Start();

        var bytes = matrix.ToMemoryPackBytes();

        timer.Stop();
        Console.WriteLine($"Serialization took: {timer.ElapsedMilliseconds} ms");

        timer.Reset();
        timer.Start();

        var matrix1 = bytes.FromMemoryPackBytes<WitMatrixSparse<double>>();

        timer.Stop();
        Console.WriteLine($"Deserialization took: {timer.ElapsedMilliseconds} ms");

    }
}