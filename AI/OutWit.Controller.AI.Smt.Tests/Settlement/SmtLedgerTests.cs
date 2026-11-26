using System.Text;
using OutWit.Controller.AI.Smt.Settlement;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The ledger a run's documents are handed out of: a section is read back as it was written, and a
/// blob that is not a ledger of this version, or that ends inside a section, is refused with words
/// that say so, by every reader of it. A refused blob leaves its file free and makes no file of a
/// section.
/// </summary>
[TestFixture]
public sealed class SmtLedgerTests
{
    #region Constants

    private const string NOT_A_LEDGER = "not an Smt ledger";
    private const string ENDS_INSIDE = "ends inside a section";

    private static readonly string[] TEXTS = ["results", "the report", "", "the integrity document", "models"];

    #endregion

    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_ledger_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(m_folder, recursive: true);
    }

    #endregion

    #region Ledger Tests

    [Test]
    public void EverySectionIsReadBackAsItWasWrittenTest()
    {
        var ledger = Ledger();

        Assert.Multiple(() =>
        {
            for (var section = 0; section < TEXTS.Length; section++)
            {
                Assert.That(Encoding.UTF8.GetString(SmtLedger.Read(ledger, section)), Is.EqualTo(TEXTS[section]), $"section {section}");

                var copy = Path.Combine(m_folder, $"copy-{section}");
                SmtLedger.CopyTo(ledger, section, copy);
                Assert.That(File.ReadAllText(copy), Is.EqualTo(TEXTS[section]), $"section {section}, copied");
            }
        });
    }

    [TestCase("an empty blob", new byte[0])]
    [TestCase("a header cut short", new byte[] { 0x57, 0x53, 0x4C })]
    [TestCase("another magic", new byte[] { 0x57, 0x53, 0x4C, 0x48, 1, 0, 0, 0 })]
    [TestCase("another version", new byte[] { 0x57, 0x53, 0x4C, 0x47, 2, 0, 0, 0 })]
    [TestCase("a text", new byte[] { 0x7B, 0x22, 0x73, 0x63, 0x68, 0x65, 0x6D, 0x61, 0x22, 0x7D })]
    public void ABlobThatIsNotALedgerIsRefusedTest(string what, byte[] bytes)
    {
        var path = Path.Combine(m_folder, "blob");
        File.WriteAllBytes(path, bytes);

        AssertRefused(path, SmtLedger.INTEGRITY, NOT_A_LEDGER, what);
    }

    [TestCase(8 + 3, "inside the length of the first section")]
    [TestCase(8 + 8 + 3, "inside the first section")]
    [TestCase(-1, "inside the length of the last section")]
    [TestCase(-2, "inside the last section")]
    public void ALedgerThatEndsInsideASectionIsRefusedTest(int keep, string where)
    {
        // A whole ledger cut short: a positive number keeps that many bytes from the start, a
        // negative one cuts the last section's bytes off (-2) or the last length as well (-1).
        var ledger = Ledger();
        var bytes = File.ReadAllBytes(ledger);
        var last = TEXTS[^1].Length;
        var length = keep switch
        {
            -1 => bytes.Length - last - 3,
            -2 => bytes.Length - 2,
            _ => keep
        };

        File.WriteAllBytes(ledger, bytes[..length]);

        AssertRefused(ledger, SmtLedger.MODELS, ENDS_INSIDE, where);
    }

    [Test]
    public void ASectionLengthBelowZeroIsRefusedTest()
    {
        var ledger = Ledger();
        var bytes = File.ReadAllBytes(ledger);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 8), -1L);
        File.WriteAllBytes(ledger, bytes);

        AssertRefused(ledger, SmtLedger.RESULTS, ENDS_INSIDE, "a negative length");
    }

    [Test]
    public void ASectionTheLedgerDoesNotHaveIsRefusedTest()
    {
        var ledger = Ledger();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SmtLedger.Read(ledger, TEXTS.Length));
            Assert.Throws<ArgumentOutOfRangeException>(() => SmtLedger.CopyTo(ledger, -1, Path.Combine(m_folder, "copy")));
            Assert.That(File.Exists(Path.Combine(m_folder, "copy")), Is.False);
        });
    }

    #endregion

    #region Tools

    /// <summary>A ledger of the five sections of <see cref="TEXTS"/>.</summary>
    private string Ledger()
    {
        var sections = TEXTS.Select((text, section) =>
        {
            var path = Path.Combine(m_folder, $"section-{section}");
            File.WriteAllText(path, text);
            return path;
        }).ToList();

        var ledger = Path.Combine(m_folder, $"{Guid.NewGuid():N}.wsl");
        SmtLedger.Write(ledger, sections);
        return ledger;
    }

    /// <summary>Every reader of a ledger refuses the blob with the words given, leaves the file free, and makes no copy.</summary>
    private void AssertRefused(string path, int section, string words, string what)
    {
        var copy = Path.Combine(m_folder, "copy");

        Assert.Multiple(() =>
        {
            Assert.That(() => SmtLedger.Read(path, section), Throws.TypeOf<InvalidDataException>().With.Message.Contains(words), $"{what}: read");
            Assert.That(() => SmtLedger.CopyTo(path, section, copy), Throws.TypeOf<InvalidDataException>().With.Message.Contains(words), $"{what}: copied");
            Assert.That(() => SmtLedger.Open(path, section, out _).Dispose(), Throws.TypeOf<InvalidDataException>().With.Message.Contains(words), $"{what}: opened");
            Assert.That(File.Exists(copy), Is.False, $"{what}: no file of the section is made");
            Assert.DoesNotThrow(() => File.Delete(path), $"{what}: the blob's file is not held open");
        });
    }

    #endregion
}
