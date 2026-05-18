using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;

namespace OutWit.Controller.Matrices.Tests.Model;

[TestFixture]
public class WitVectorSparseTests
{
    [Test]
    public void ConstructorsTest()
    {
        var elements = new[] { (0, 10), (2, 20), (4, 30) };

        var vector = WitVectorSparse<int>.Create(5, elements, VectorType.Row);

        Assert.That(vector.Count, Is.EqualTo(5));
        Assert.That(vector.Values, Is.EqualTo([10, 20, 30]));
        Assert.That(vector.Indices, Is.EqualTo([0, 2, 4]));
        Assert.That(vector.Type, Is.EqualTo(VectorType.Row));
        Assert.That(vector.GetNonZeroElements().Count(), Is.EqualTo(3));
    }

    [Test]
    public void CreateHandlesUnsortedAndZerosTest()
    {
        var elements = new[] { (3, 30), (1, 10), (2, 0), (0, 5) };

        var vector = WitVectorSparse<int>.Create(4, elements);

        var nonZero = vector.GetNonZeroElements().ToList();
        Assert.That(nonZero.Count, Is.EqualTo(3));
        Assert.That(nonZero[0], Is.EqualTo((0, 5)));
        Assert.That(nonZero[1], Is.EqualTo((1, 10)));
        Assert.That(nonZero[2], Is.EqualTo((3, 30)));
    }

    [Test]
    public void IsTest()
    {
        var vectorA = WitVectorSparse<int>.Create(5, new[] { (1, 10), (3, 30) });
        var vectorB = WitVectorSparse<int>.Create(5, new[] { (1, 10), (3, 30) });
        var vectorC = WitVectorSparse<int>.Create(5, new[] { (1, 10), (3, 99) }); // Different value
        var vectorD = WitVectorSparse<int>.Create(6, new[] { (1, 10), (3, 30) }); // Different dimension
        var vectorE = WitVectorSparse<int>.Create(5, new[] { (1, 10), (4, 30) }); // Different index
        
        Assert.That(vectorA, Was.EqualTo(vectorB));
        Assert.That(vectorA, Was.Not.EqualTo(vectorC));
        Assert.That(vectorA, Was.Not.EqualTo(vectorD));
        Assert.That(vectorA, Was.Not.EqualTo(vectorE));
    }

    [Test]
    public void CloneTest()
    {
        var original = WitVectorSparse<int>.Create(5, new[] { (1, 10), (3, 30) });
        WitVectorSparse<int> clone = original.Clone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.Indices, Is.EqualTo(original.Indices));
        Assert.That(clone.Values, Is.EqualTo(original.Values));
        Assert.That(clone.Count, Is.EqualTo(original.Count));
        Assert.That(clone.Type, Is.EqualTo(original.Type));
    }
    
    [Test]
    public void MemoryPackCloneTest()
    {
        var original = WitVectorSparse<int>.Create(5, new[] { (1, 10), (3, 30) });
        WitVectorSparse<int> clone = original.MemoryPackClone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.Indices, Is.EqualTo(original.Indices));
        Assert.That(clone.Values, Is.EqualTo(original.Values));
        Assert.That(clone.Count, Is.EqualTo(original.Count));
        Assert.That(clone.Type, Is.EqualTo(original.Type));
    }

    [Test]
    public void IndexerGetTest()
    {
        var elements = new[] { (1, 100), (3, 300) };
        var vector = WitVectorSparse<int>.Create(5, elements);

        Assert.That(vector[0], Is.EqualTo(0));
        Assert.That(vector[1], Is.EqualTo(100));
        Assert.That(vector[2], Is.EqualTo(0));
        Assert.That(vector[3], Is.EqualTo(300));
        Assert.That(vector[4], Is.EqualTo(0));
        Assert.Throws<IndexOutOfRangeException>(() => { var _ = vector[5]; });
    }

    [Test]
    public void IndexerSetThrowsExceptionTest()
    {
        var vector = WitVectorSparse<int>.Create(5, new[] { (1, 100) });

        Assert.Throws<NotSupportedException>(() => { vector[0] = 50; });
    }

    [Test]
    public void GetNonZeroElementsTest()
    {
        var elements = new[] { (0, 10), (2, 20), (4, 30) };
        var vector = WitVectorSparse<int>.Create(5, elements);

        var nonZero = vector.GetNonZeroElements().ToList();

        Assert.That(nonZero.Count, Is.EqualTo(3));
        Assert.That(nonZero, Is.EquivalentTo(elements));
    }

    [Test]
    public void EnumeratorTest()
    {
        var elements = new[] { (1, 10), (3, 30) };
        var vector = WitVectorSparse<int>.Create(5, elements);
        var expectedFullVector = new[] { 0, 10, 0, 30, 0 };

        var enumeratedList = vector.ToList();

        Assert.That(enumeratedList, Is.EqualTo(expectedFullVector));
    }
}