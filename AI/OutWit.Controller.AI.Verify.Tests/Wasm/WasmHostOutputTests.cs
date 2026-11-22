using System.Text;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// A guest's output stream read while the run goes on: the bytes kept from a position, as a
/// conversation follows its guest's stdout. A reader sees what the cap kept and nothing of what
/// it dropped, and a stream that goes to a file shows nothing.
/// </summary>
[TestFixture]
public sealed class WasmHostOutputTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmoutput_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Kept From Tests

    [Test]
    public void TheKeptBytesAreReadFromAPositionOnTest()
    {
        using var output = new WasmHostOutput(0);
        output.Write("hello"u8);

        Assert.Multiple(() =>
        {
            Assert.That(Text(output, 0), Is.EqualTo("hello"));
            Assert.That(Text(output, 2), Is.EqualTo("llo"));
            Assert.That(Text(output, 5), Is.Empty, "everything was seen");
            Assert.That(Text(output, 99), Is.Empty, "a position past the end reads nothing");
            Assert.That(Text(output, -3), Is.EqualTo("hello"), "a position before the start reads from the start");
        });
    }

    [Test]
    public void AReaderThatFollowsTheStreamSeesEachWriteOnceTest()
    {
        using var output = new WasmHostOutput(0);
        var seen = new List<string>();

        foreach (var piece in new[] { "ab", "", "cde", "f" })
        {
            var position = output.Kept;
            output.Write(Encoding.UTF8.GetBytes(piece));
            seen.Add(Text(output, position));
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new[] { "ab", "", "cde", "f" }));
            Assert.That(output.Text(), Is.EqualTo("abcdef"), "reading from a position changes nothing of the stream");
        });
    }

    [Test]
    public void NothingPastTheCapIsReadTest()
    {
        using var output = new WasmHostOutput(4);
        output.Write("hello"u8);
        var position = output.Kept;
        output.Write("more"u8);

        Assert.Multiple(() =>
        {
            Assert.That(Text(output, 0), Is.EqualTo("hell"));
            Assert.That(Text(output, position), Is.Empty, "what was written after the cap was dropped");
            Assert.That(output.Cut, Is.True);
        });
    }

    [Test]
    public void AStreamThatKeepsNothingShowsNothingTest()
    {
        using var output = new WasmHostOutput(-1);
        output.Write("hello"u8);

        Assert.That(Text(output, 0), Is.Empty);
    }

    [Test]
    public void AStreamWrittenToAFileShowsNothingTest()
    {
        var file = Path.Combine(m_root, "stdout.bin");
        using (var output = new WasmHostOutput(file, 0))
        {
            output.Write("hello"u8);

            Assert.Multiple(() =>
            {
                Assert.That(output.Kept, Is.EqualTo(5));
                Assert.That(Text(output, 0), Is.Empty, "the bytes are in the file, not in memory");
            });
        }

        Assert.That(File.ReadAllText(file), Is.EqualTo("hello"));
    }

    #endregion

    #region Tools

    private static string Text(WasmHostOutput output, long position)
    {
        return Encoding.UTF8.GetString(output.KeptFrom(position));
    }

    #endregion
}
