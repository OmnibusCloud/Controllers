using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using OutWit.Controller.AI.Verify.Assets;

namespace OutWit.Controller.AI.Verify.Tests.Assets;

/// <summary>
/// What the asset tool refuses of an archive it unpacks (audit finding V:L-9). Every archive is
/// pinned by its SHA-256, so an entry that names a place outside its folder is not expected;
/// two of the tool's three extractors nevertheless wrote wherever an entry said (the Ruby
/// release archive and a Python wheel), so a pin moved to a bad archive would have written
/// outside the build's folders before anyone looked inside. Each extractor is given an archive
/// of the right shape with one such entry: it refuses the archive by the entry's name and
/// nothing lands outside the folder.
/// </summary>
[TestFixture]
public sealed class VerifyAssetArchiveTests
{
    #region Constants

    private const string ESCAPED = "escaped.txt";
    private const string RUBY_ROOT = "ruby-3.4-wasm32-unknown-wasip1-full/";
    private const string RUBY_LIBRARY = RUBY_ROOT + "usr/local/lib/ruby/3.4.0/";
    private const string RUBY_GEM = RUBY_ROOT + "usr/local/lib/ruby/gems/3.4.0/gems/csv-3.3.2/";
    private const string CLANG_SYSROOT = "package/gen/llvm-resources.tar";
    private const string CLANG_MODULE = "package/gen/llvm.core.wasm";

    #endregion

    #region Fields

    private string m_work = null!;

    private string m_target = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        // The folder an archive is unpacked into lies three levels down, so that an entry that
        // climbs out of it still lands inside the test's own folder, where it is looked for.
        m_work = Path.Combine(Path.GetTempPath(), $"witai_verify_archives_{Guid.NewGuid():N}");
        m_target = Path.Combine(m_work, "a", "b", "target");
        Directory.CreateDirectory(m_target);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_work))
            Directory.Delete(m_work, recursive: true);
    }

    #endregion

    #region Path Tests

    [TestCase("../escaped.txt")]
    [TestCase("lib/../../escaped.txt")]
    [TestCase("lib/./x.txt")]
    [TestCase("lib//x.txt")]
    [TestCase("/etc/escaped.txt")]
    [TestCase("C:/escaped.txt")]
    [TestCase("lib/x.txt:stream")]
    [TestCase("")]
    public void L9AnEntryThatIsNotAPlainRelativePathIsRefusedTest(string entry)
    {
        Assert.That(() => ArchivePaths.Under(m_target, entry), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => ArchivePaths.Under(m_target, entry.Replace('/', '\\')), Throws.TypeOf<InvalidDataException>(), "the same with backslashes");
    }

    [Test]
    public void L9APlainEntryIsWrittenUnderItsFolderTest()
    {
        var path = ArchivePaths.Under(m_target, "usr/local/lib/ruby/3.4.0/set.rb");

        Assert.That(path, Is.EqualTo(Path.Combine(m_target, "usr", "local", "lib", "ruby", "3.4.0", "set.rb")));
    }

    #endregion

    #region Extractor Tests

    [Test]
    public void L9TheRubyArchiveCannotWriteOutsideItsFolderTest()
    {
        var archive = Path.Combine(m_work, "ruby.tar.gz");
        WriteTarGz(archive,
        [
            (RUBY_ROOT + "usr/local/bin/ruby", "module"),
            (RUBY_LIBRARY + "set.rb", "library"),
            (RUBY_LIBRARY + "../../../../../../../../" + ESCAPED, "out"),
        ]);

        Assert.That(() => RubyRuntimeBuilder.Build(archive, m_target), Throws.TypeOf<InvalidDataException>().With.Message.Contains(ESCAPED));
        AssertNothingEscaped();
    }

    [Test]
    public void L9ABundledGemCannotWriteOutsideItsFolderTest()
    {
        var archive = Path.Combine(m_work, "ruby.tar.gz");
        WriteTarGz(archive,
        [
            (RUBY_ROOT + "usr/local/bin/ruby", "module"),
            (RUBY_GEM + "lib/../../../../../../../../../../../" + ESCAPED, "out"),
        ]);

        Assert.That(() => RubyRuntimeBuilder.Build(archive, m_target), Throws.TypeOf<InvalidDataException>().With.Message.Contains(ESCAPED));
        AssertNothingEscaped();
    }

    [Test]
    public void L9AWheelCannotWriteOutsideItsFolderTest()
    {
        var wheel = Path.Combine(m_work, "sympy.whl");
        using (var zip = ZipFile.Open(wheel, ZipArchiveMode.Create))
        {
            Add(zip, "sympy/__init__.py", "package");
            Add(zip, "sympy/../../" + ESCAPED, "out");
        }

        Assert.That(() => PythonLayerBuilder.ExtractPackage(wheel, "sympy", m_target), Throws.TypeOf<InvalidDataException>().With.Message.Contains(ESCAPED));
        AssertNothingEscaped();
    }

    [Test]
    public void L9TheSysrootCannotWriteOutsideItsFolderTest()
    {
        // The one extractor that had the check: held to it here with the others.
        var sysroot = new MemoryStream();
        using (var tar = new TarWriter(sysroot, TarEntryFormat.Pax, leaveOpen: true))
        {
            Write(tar, "usr/include/stdio.h", "header");
            Write(tar, "usr/../../../" + ESCAPED, "out");
        }

        var package = Path.Combine(m_work, "clang.tgz");
        using (var file = File.Create(package))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            Write(tar, CLANG_MODULE, "module");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, CLANG_SYSROOT) { DataStream = new MemoryStream(sysroot.ToArray()) });
        }

        Assert.That(() => ClangRuntimeBuilder.Build(package, m_target), Throws.TypeOf<InvalidDataException>().With.Message.Contains(ESCAPED));
        AssertNothingEscaped();
    }

    [Test]
    public void L9AnArchiveOfPlainEntriesIsUnpackedTest()
    {
        // The control: the same shape without the bad entry is taken.
        var archive = Path.Combine(m_work, "ruby.tar.gz");
        WriteTarGz(archive,
        [
            (RUBY_ROOT + "usr/local/bin/ruby", "module"),
            (RUBY_LIBRARY + "set.rb", "library"),
            (RUBY_GEM + "lib/csv.rb", "gem"),
        ]);

        var summary = RubyRuntimeBuilder.Build(archive, m_target);

        Assert.Multiple(() =>
        {
            Assert.That(summary.LibraryFiles, Is.EqualTo(1));
            Assert.That(summary.GemFiles, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(m_target, RubyRuntimeBuilder.TREE_DIR, "usr", "local", "lib", "ruby", "3.4.0", "set.rb")), Is.EqualTo("library"));
        });
    }

    #endregion

    #region Tools

    private void AssertNothingEscaped()
    {
        var escaped = Directory.GetFiles(m_work, ESCAPED, SearchOption.AllDirectories);

        Assert.That(escaped, Is.Empty, "an entry of the archive was written outside the folder it was unpacked into");
    }

    private static void WriteTarGz(string path, IReadOnlyList<(string Name, string Content)> entries)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
        foreach (var (name, content) in entries)
            Write(tar, name, content);
    }

    private static void Write(TarWriter tar, string name, string content)
    {
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    #endregion
}
