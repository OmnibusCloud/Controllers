using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Controller.Matrices.Tests.Utils;

namespace OutWit.Controller.Matrices.Tests.Model;

[TestFixture]
public class WitMatrixTests
{
    [Test]
    public void CreateTest()
    {
        var data = new[] { 1, 2, 3, 4, 5, 6 };
        var matrix = WitMatrix<int>.Create(2, 3, data);

        Assert.That(matrix.RowCount, Is.EqualTo(2));
        Assert.That(matrix.ColumnCount, Is.EqualTo(3));
        Assert.That(matrix.Data, Is.EqualTo(data));
        Assert.That(matrix[1, 2], Is.EqualTo(6));

        var zeroMatrix = WitMatrix<int>.Create(3, 2);

        Assert.That(zeroMatrix.RowCount, Is.EqualTo(3));
        Assert.That(zeroMatrix.ColumnCount, Is.EqualTo(2));
        Assert.That(zeroMatrix.Data, Is.All.EqualTo(0));
    }

    [Test]
    public void CreateDefensiveCopyTest()
    {
        var originalData = new[] { 1, 2, 3, 4 };
        var matrix = WitMatrix<int>.Create(2, 2, originalData);

        originalData[0] = 99;

        Assert.That(matrix[0, 0], Is.EqualTo(1));
    }

    [Test]
    public void CreateIdentityTest()
    {
        var identity = WitMatrix<int>.CreateIdentity(3);

        Assert.That(identity.RowCount, Is.EqualTo(3));
        Assert.That(identity.ColumnCount, Is.EqualTo(3));
        Assert.That(identity[0, 0], Is.EqualTo(1));
        Assert.That(identity[1, 1], Is.EqualTo(1));
        Assert.That(identity[2, 2], Is.EqualTo(1));
        Assert.That(identity[0, 1], Is.EqualTo(0));
    }

    [Test]
    public void IsTest()
    {
        var matrixA = WitMatrix<int>.Create(2, 2, [1, 2, 3, 4]);
        var matrixB = WitMatrix<int>.Create(2, 2, [1, 2, 3, 4]);
        var matrixC = WitMatrix<int>.Create(2, 2, [1, 2, 3, 5]);
        var matrixD = WitMatrix<int>.Create(2, 3, [1, 2, 3, 4, 5, 6]);

        Assert.That(matrixA, Was.EqualTo(matrixB));
        Assert.That(matrixA, Was.Not.EqualTo(matrixC));
        Assert.That(matrixA, Was.Not.EqualTo(matrixD));
        Assert.That(matrixA, Was.Not.EqualTo(null));
        
    }

    [Test]
    public void CloneTest()
    {
        var original = WitMatrix<int>.Create(2, 2, [1, 2, 3, 4]);
        WitMatrix<int> clone = original.Clone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.RowCount, Is.EqualTo(original.RowCount));
        Assert.That(clone.ColumnCount, Is.EqualTo(original.ColumnCount));
        Assert.That(clone.Data, Is.EqualTo(original.Data));
    }
    
    [Test]
    public void MemoryPackCloneTest()
    {
        var original = WitMatrix<int>.Create(2, 2, [1, 2, 3, 4]);
        WitMatrix<int> clone = original.MemoryPackClone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.RowCount, Is.EqualTo(original.RowCount));
        Assert.That(clone.ColumnCount, Is.EqualTo(original.ColumnCount));
        Assert.That(clone.Data, Is.EqualTo(original.Data));
    }

    [Test]
    public void IndexerGetSetTest()
    {
        var matrix = WitMatrix<int>.Create(2, 2);

        matrix[0, 0] = 10;
        matrix[0, 1] = 20;
        matrix[1, 0] = 30;
        matrix[1, 1] = 40;

        Assert.That(matrix[0, 0], Is.EqualTo(10));
        Assert.That(matrix[0, 1], Is.EqualTo(20));
        Assert.That(matrix[1, 0], Is.EqualTo(30));
        Assert.That(matrix[1, 1], Is.EqualTo(40));
        Assert.Throws<IndexOutOfRangeException>(() => { var x = matrix[2, 0]; });
    }

    [Test]
    public void GetRowTest()
    {
        var matrix = WitMatrix<int>.Create(2, 3, new[] { 1, 2, 3, 4, 5, 6 });

        var row1 = matrix.GetRow(1);

        Assert.That(row1, Is.Not.Null);
        Assert.That(row1.Count, Is.EqualTo(3));
        Assert.That(row1.Type, Is.EqualTo(VectorType.Row));
        Assert.That(row1.ToArray(), Is.EqualTo(new[] { 4, 5, 6 }));
    }

    [Test]
    public void GetColumnTest()
    {
        var matrix = WitMatrix<int>.Create(2, 3, new[] { 1, 2, 3, 4, 5, 6 });

        var col1 = matrix.GetColumn(1);

        Assert.That(col1, Is.Not.Null);
        Assert.That(col1.Count, Is.EqualTo(2));
        Assert.That(col1.Type, Is.EqualTo(VectorType.Column));
        Assert.That(col1.ToArray(), Is.EqualTo(new[] { 2, 5 }));
    }

    [Test]
    public void TransposeTest()
    {
        var matrix = WitMatrix<int>.Create(2, 3, new[] { 1, 2, 3, 4, 5, 6 });

        var transposed = matrix.Transpose();

        Assert.That(transposed.RowCount, Is.EqualTo(3));
        Assert.That(transposed.ColumnCount, Is.EqualTo(2));
        Assert.That(transposed[0, 0], Is.EqualTo(1));
        Assert.That(transposed[1, 0], Is.EqualTo(2));
        Assert.That(transposed[2, 0], Is.EqualTo(3));
        Assert.That(transposed[0, 1], Is.EqualTo(4));
        Assert.That(transposed[1, 1], Is.EqualTo(5));
        Assert.That(transposed[2, 1], Is.EqualTo(6));
    }
    
    [Test]
    public void SaveLoadTest()
    {
        var originalMatrix = WitMatrix<double>.Create(2, 2, new[] { 1.1, 2.2, 3.3, 4.4 });
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mat");

        try
        {
            originalMatrix.Save(filePath);
            var loadedMatrix = WitMatrix<double>.Load(filePath);

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
    public async Task SaveLoadAsyncTest()
    {
        var originalMatrix = WitMatrix<float>.Create(3, 2, new[] { 1.1f, 2.2f, 3.3f, 4.4f, 5.5f, 6.6f });
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mat");

        try
        {
            await originalMatrix.SaveAsync(filePath);
            var loadedMatrix = await WitMatrix<float>.LoadAsync(filePath);

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
    public void RandomMatrixSaveLoadTest()
    {
        var originalMatrix = MatrixUtils.RandomMatrix(10, 10, 123);
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mat");

        try
        {
            originalMatrix.Save(filePath);
            var loadedMatrix = WitMatrix<double>.Load(filePath);

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
}