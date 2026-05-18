using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;

namespace OutWit.Controller.Matrices.Tests.Model;

[TestFixture]
public class WitVectorTests
{
    [Test]
    public void ConstructorsTest()
    {
        var sourceData = new[] { 1, 2, 3 };

        var colVector = WitVector<int>.Create(sourceData, VectorType.Column);
        var rowVector = WitVector<int>.Create(sourceData, VectorType.Row);
        var emptyVector = WitVector<int>.Create(3);

        Assert.That(colVector.Count, Is.EqualTo(3));
        Assert.That(colVector.Type, Is.EqualTo(VectorType.Column));
        Assert.That(colVector.Data, Is.EqualTo(sourceData));

        Assert.That(rowVector.Count, Is.EqualTo(3));
        Assert.That(rowVector.Type, Is.EqualTo(VectorType.Row));

        Assert.That(emptyVector.Count, Is.EqualTo(3));
        Assert.That(emptyVector.Data, Is.EqualTo(new[] { 0, 0, 0 }));
    }

    [Test]
    public void CreateMakesDefensiveCopyTest()
    {
        var originalData = new[] { 10, 20, 30 };
        var vector = WitVector<int>.Create(originalData);

        originalData[0] = 99;

        Assert.That(vector[0], Is.EqualTo(10));
    }

    [Test]
    public void IsTest()
    {
        var vectorA = WitVector<int>.Create([1, 2, 3]);
        var vectorB = WitVector<int>.Create([1, 2, 3]);
        var vectorC = WitVector<int>.Create([1, 2, 4]);
        var vectorD = WitVector<int>.Create([1, 2, 3], VectorType.Column);

        Assert.That(vectorA, Was.EqualTo(vectorB));
        Assert.That(vectorA, Was.Not.EqualTo(vectorC));
        Assert.That(vectorA, Was.Not.EqualTo(vectorD));
        Assert.That(vectorA, Was.Not.EqualTo(null));
    }

    [Test]
    public void CloneTest()
    {
        WitVector<int> original = WitVector<int>.Create([5, 10, 15]);
        WitVector<int> clone = original.Clone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.Data, Is.EqualTo(original.Data));
    }
    
    [Test]
    public void MemoryPackCloneTest()
    {
        WitVector<int> original = WitVector<int>.Create([5, 10, 15]);
        WitVector<int> clone = original.MemoryPackClone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone, Was.EqualTo(original));
        Assert.That(clone.Data, Is.EqualTo(original.Data));
    }

    [Test]
    public void IndexerGetSetTest()
    {
        var vector = WitVector<double>.Create(3);

        vector[0] = 1.1;
        vector[1] = 2.2;
        vector[2] = 3.3;

        Assert.That(vector[0], Is.EqualTo(1.1));
        Assert.That(vector[1], Is.EqualTo(2.2));
        Assert.That(vector[2], Is.EqualTo(3.3));
        Assert.Throws<IndexOutOfRangeException>(() => { var x = vector[3]; });
        Assert.Throws<IndexOutOfRangeException>(() => { vector[-1] = 0; });
    }

    [Test]
    public void EnumeratorTest()
    {
        var data = new[] { 10, 20, 30 };
        var vector = WitVector<int>.Create(data);
        var enumeratedList = new List<int>();

        foreach (var item in vector)
            enumeratedList.Add(item);

        Assert.That(enumeratedList, Is.EqualTo(data));
    }
}