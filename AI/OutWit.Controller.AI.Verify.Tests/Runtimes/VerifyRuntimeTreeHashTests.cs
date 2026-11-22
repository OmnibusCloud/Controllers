using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Runtimes;

/// <summary>
/// The tree hash that pins a runtime's library folder: it is a function of the files' names
/// and contents only (the same tree gives the same value wherever it lies and in whatever
/// order it was written), and any change to a name, to a content or to the set of files
/// gives another value.
/// </summary>
[TestFixture]
public sealed class VerifyRuntimeTreeHashTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_tree_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Format Tests

    [Test]
    public void TheHashIsTheDocumentedFormatTest()
    {
        // Two files, fed in ordinal order of the '/'-separated relative path:
        // path bytes, a zero byte, the length as eight little-endian bytes, the bytes.
        var tree = Tree("a", ("lib/b.rb", "puts 2"), ("a.rb", "hi"));

        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, content) in new[] { ("a.rb", "hi"), ("lib/b.rb", "puts 2") })
        {
            var length = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(length, content.Length);
            expected.AppendData(Encoding.UTF8.GetBytes(path));
            expected.AppendData([0]);
            expected.AppendData(length);
            expected.AppendData(Encoding.UTF8.GetBytes(content));
        }

        Assert.That(VerifyRuntimeTreeHash.Compute(tree), Is.EqualTo(Convert.ToHexStringLower(expected.GetHashAndReset())));
    }

    [Test]
    public void TheSameTreeGivesTheSameHashWhereverItLiesTest()
    {
        var first = Tree("first", ("x/one.rb", "1"), ("x/y/two.rb", "22"), ("three.rb", ""));
        var second = Tree("second", ("three.rb", ""), ("x/y/two.rb", "22"), ("x/one.rb", "1"));

        Assert.That(VerifyRuntimeTreeHash.Compute(second), Is.EqualTo(VerifyRuntimeTreeHash.Compute(first)));
    }

    [Test]
    public void AnEmptyTreeHasAHashTest()
    {
        var tree = Tree("empty");

        Assert.That(VerifyRuntimeTreeHash.Compute(tree), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData([]))));
    }

    #endregion

    #region Change Tests

    [Test]
    public void AChangedByteChangesTheHashTest()
    {
        var original = Tree("original", ("a.rb", "puts 1"), ("b.rb", "puts 2"));
        var changed = Tree("changed", ("a.rb", "puts 1"), ("b.rb", "puts 3"));

        Assert.That(VerifyRuntimeTreeHash.Compute(changed), Is.Not.EqualTo(VerifyRuntimeTreeHash.Compute(original)));
    }

    [Test]
    public void ARenamedFileChangesTheHashTest()
    {
        var original = Tree("original", ("a.rb", "puts 1"));
        var renamed = Tree("renamed", ("b.rb", "puts 1"));
        var moved = Tree("moved", ("lib/a.rb", "puts 1"));

        Assert.Multiple(() =>
        {
            Assert.That(VerifyRuntimeTreeHash.Compute(renamed), Is.Not.EqualTo(VerifyRuntimeTreeHash.Compute(original)));
            Assert.That(VerifyRuntimeTreeHash.Compute(moved), Is.Not.EqualTo(VerifyRuntimeTreeHash.Compute(original)));
        });
    }

    [Test]
    public void AnAddedEmptyFileChangesTheHashTest()
    {
        var original = Tree("original", ("a.rb", "puts 1"));
        var added = Tree("added", ("a.rb", "puts 1"), ("z.rb", ""));

        Assert.That(VerifyRuntimeTreeHash.Compute(added), Is.Not.EqualTo(VerifyRuntimeTreeHash.Compute(original)));
    }

    [Test]
    public void BytesMovedBetweenNeighbouringFilesChangeTheHashTest()
    {
        // The length in front of every file keeps the boundaries: the same bytes cut
        // differently between two files are another tree.
        var original = Tree("original", ("a", "xy"), ("b", "z"));
        var shifted = Tree("shifted", ("a", "x"), ("b", "yz"));

        Assert.That(VerifyRuntimeTreeHash.Compute(shifted), Is.Not.EqualTo(VerifyRuntimeTreeHash.Compute(original)));
    }

    [Test]
    public void AMissingFolderIsAnErrorTest()
    {
        Assert.That(() => VerifyRuntimeTreeHash.Compute(Path.Combine(m_root, "absent")), Throws.TypeOf<DirectoryNotFoundException>());
    }

    #endregion

    #region Tools

    private string Tree(string name, params (string Path, string Content)[] files)
    {
        var tree = Path.Combine(m_root, name);
        Directory.CreateDirectory(tree);
        foreach (var (path, content) in files)
        {
            var file = Path.Combine(tree, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content, new UTF8Encoding(false));
        }

        return tree;
    }

    #endregion
}
