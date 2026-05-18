using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.Assets.Pack.Csproj;
using OutWit.Controller.Assets.Pack.Packers;

namespace OutWit.Controller.Assets.Pack.Tests.Packers
{
    [TestFixture]
    public class AssetPackerTests
    {
        #region Fields

        private string m_tempDir = null!;
        private string m_prereqs = null!;
        private string m_output = null!;
        private AssetPacker m_packer = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "outwit-assets-pack-tests-" + Guid.NewGuid().ToString("N"));
            m_prereqs = Path.Combine(m_tempDir, "prereqs");
            m_output  = Path.Combine(m_tempDir, "out");
            Directory.CreateDirectory(m_prereqs);
            m_packer = new AssetPacker();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempDir))
                Directory.Delete(m_tempDir, recursive: true);
        }

        #endregion

        #region Zip-folder packing

        [Test]
        public void PackZipFolderProducesZipWithExpectedFilesTest()
        {
            var srcDir = Path.Combine(m_prereqs, "tool", "win-x64");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "tool.exe"),    "tool-binary");
            Directory.CreateDirectory(Path.Combine(srcDir, "data"));
            File.WriteAllText(Path.Combine(srcDir, "data", "x.dat"), "data-payload");

            var entry = NewEntry(
                id: "tool-win-x64",
                uri: "gh-release://owner/repo/tag/tool-win-x64.zip",
                packSource: "tool/win-x64",
                packKind: ControllerDataAssetEntry.PackKindZipFolder);

            var result = m_packer.Pack(entry, m_prereqs, m_output);

            Assert.That(File.Exists(result.ArtifactPath), Is.True);
            Assert.That(result.ArtifactName, Is.EqualTo("tool-win-x64.zip"));
            Assert.That(result.Reused, Is.False);

            using var archive = ZipFile.OpenRead(result.ArtifactPath);
            var entries = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).OrderBy(s => s).ToList();
            Assert.That(entries, Has.Member("tool.exe"));
            Assert.That(entries.Any(e => e == "data/x.dat"), Is.True);
        }

        #endregion

        #region Single-file copy

        [Test]
        public void PackSingleFileCopiesVerbatimTest()
        {
            var srcFile = Path.Combine(m_prereqs, "benchmark", "scene.blend");
            Directory.CreateDirectory(Path.GetDirectoryName(srcFile)!);
            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            File.WriteAllBytes(srcFile, payload);

            var entry = NewEntry(
                id: "benchmark-scene",
                uri: "gh-release://owner/repo/tag/scene.blend",
                packSource: "benchmark/scene.blend",
                packKind: ControllerDataAssetEntry.PackKindSingleFile);

            var result = m_packer.Pack(entry, m_prereqs, m_output);

            Assert.That(result.ArtifactName, Is.EqualTo("scene.blend"));
            Assert.That(File.ReadAllBytes(result.ArtifactPath), Is.EqualTo(payload));
            Assert.That(result.Size, Is.EqualTo(payload.Length));
        }

        #endregion

        #region Reuse + clean

        [Test]
        public void PackReusesExistingArtifactByDefaultTest()
        {
            var srcFile = Path.Combine(m_prereqs, "x.bin");
            File.WriteAllBytes(srcFile, new byte[] { 1, 2, 3 });

            var entry = NewEntry("x", "gh-release://o/r/t/x.bin", "x.bin", ControllerDataAssetEntry.PackKindSingleFile);

            var first = m_packer.Pack(entry, m_prereqs, m_output);
            Assert.That(first.Reused, Is.False);

            var second = m_packer.Pack(entry, m_prereqs, m_output);
            Assert.That(second.Reused, Is.True);
            Assert.That(second.Sha256, Is.EqualTo(first.Sha256));
        }

        [Test]
        public void PackForceCleanRePacksArtifactTest()
        {
            var srcFile = Path.Combine(m_prereqs, "x.bin");
            File.WriteAllBytes(srcFile, new byte[] { 1, 2, 3 });

            var entry = NewEntry("x", "gh-release://o/r/t/x.bin", "x.bin", ControllerDataAssetEntry.PackKindSingleFile);

            m_packer.Pack(entry, m_prereqs, m_output);
            var result = m_packer.Pack(entry, m_prereqs, m_output, forceClean: true);

            Assert.That(result.Reused, Is.False);
        }

        #endregion

        #region SHA accuracy

        [Test]
        public void PackComputesCorrectSha256ForSingleFileTest()
        {
            var srcFile = Path.Combine(m_prereqs, "payload.bin");
            var bytes = Encoding.UTF8.GetBytes("hello-controller");
            File.WriteAllBytes(srcFile, bytes);

            var entry = NewEntry("p", "gh-release://o/r/t/payload.bin", "payload.bin", ControllerDataAssetEntry.PackKindSingleFile);
            var result = m_packer.Pack(entry, m_prereqs, m_output);

            var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.That(result.Sha256, Is.EqualTo(expected));
        }

        #endregion

        #region Errors

        [Test]
        public void PackThrowsWhenPackSourceMissingMetadataTest()
        {
            var entry = NewEntry(
                id: "no-source",
                uri: "gh-release://o/r/t/x.bin",
                packSource: null,
                packKind: ControllerDataAssetEntry.PackKindSingleFile);

            Assert.That(() => m_packer.Pack(entry, m_prereqs, m_output),
                        Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void PackThrowsForZipFolderWhenSourceDirectoryMissingTest()
        {
            var entry = NewEntry(
                id: "missing-dir",
                uri: "gh-release://o/r/t/missing.zip",
                packSource: "does/not/exist",
                packKind: ControllerDataAssetEntry.PackKindZipFolder);

            Assert.That(() => m_packer.Pack(entry, m_prereqs, m_output),
                        Throws.InstanceOf<DirectoryNotFoundException>());
        }

        [Test]
        public void ArtifactNameFromUriExtractsTrailingSegmentTest()
        {
            Assert.That(
                AssetPacker.ArtifactNameFromUri("gh-release://owner/repo/tag/blender-windows-x64.zip", "id"),
                Is.EqualTo("blender-windows-x64.zip"));
        }

        [Test]
        public void ArtifactNameFromUriThrowsOnEmptyUriTest()
        {
            Assert.That(() => AssetPacker.ArtifactNameFromUri("", "x"), Throws.InstanceOf<InvalidOperationException>());
        }

        #endregion

        #region Tools

        private static ControllerDataAssetEntry NewEntry(
            string id, string uri, string? packSource, string packKind)
        {
            return new ControllerDataAssetEntry
            {
                Id = id,
                Uri = uri,
                PackSource = packSource,
                PackKind = packKind,
            };
        }

        #endregion
    }
}
