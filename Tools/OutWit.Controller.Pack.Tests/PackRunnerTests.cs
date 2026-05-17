using System.IO.Compression;
using System.Text;
using OutWit.Controller.Pack.Options;

namespace OutWit.Controller.Pack.Tests
{
    [TestFixture]
    public class PackRunnerTests
    {
        #region Fields

        private string m_tempDir = null!;
        private string m_moduleDir = null!;
        private string m_outputDir = null!;
        private TextWriter m_originalOut = null!;
        private TextWriter m_originalErr = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "outwit-pack-" + Guid.NewGuid().ToString("N"));
            m_moduleDir = Path.Combine(m_tempDir, "demo.module");
            m_outputDir = Path.Combine(m_tempDir, "out");
            Directory.CreateDirectory(m_moduleDir);
            Directory.CreateDirectory(m_outputDir);

            m_originalOut = Console.Out;
            m_originalErr = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(m_originalOut);
            Console.SetError(m_originalErr);
            if (Directory.Exists(m_tempDir))
                Directory.Delete(m_tempDir, recursive: true);
        }

        #endregion

        [Test]
        public void ThrowsOnMissingModuleDirTest()
        {
            var opts = new PackOptions { ModuleDir = Path.Combine(m_tempDir, "does-not-exist") };
            Assert.ThrowsAsync<DirectoryNotFoundException>(() => PackRunner.RunAsync(opts));
        }

        [Test]
        public void ThrowsOnMissingManifestTest()
        {
            var opts = new PackOptions { ModuleDir = m_moduleDir };
            Assert.ThrowsAsync<FileNotFoundException>(() => PackRunner.RunAsync(opts));
        }

        [Test]
        public async Task PacksManifestWithoutDataAssetsTest()
        {
            WriteManifest("""
                {
                  "name": "Demo",
                  "version": "1.0.0"
                }
                """);
            File.WriteAllText(Path.Combine(m_moduleDir, "Demo.dll"), "fake-dll-bytes");

            var output = Path.Combine(m_outputDir, "demo.zip");
            await PackRunner.RunAsync(new PackOptions { ModuleDir = m_moduleDir, OutputPath = output });

            Assert.That(File.Exists(output), Is.True);
            using var zip = ZipFile.OpenRead(output);
            Assert.That(zip.Entries.Select(e => e.FullName),
                Is.SupersetOf(new[] { PackRunner.MANIFEST_FILENAME, "Demo.dll" }));
        }

        [Test]
        public async Task DefaultsOutputPathToNameVersionZipTest()
        {
            WriteManifest("""
                {
                  "name": "Demo",
                  "version": "2.5.0"
                }
                """);

            var prev = Environment.CurrentDirectory;
            Environment.CurrentDirectory = m_outputDir;
            try
            {
                await PackRunner.RunAsync(new PackOptions { ModuleDir = m_moduleDir });
                Assert.That(File.Exists(Path.Combine(m_outputDir, "Demo-2.5.0.zip")), Is.True);
            }
            finally
            {
                Environment.CurrentDirectory = prev;
            }
        }

        [Test]
        public async Task PacksManifestWithInlineFileAssetTest()
        {
            var assetsDir = Path.Combine(m_moduleDir, "assets");
            Directory.CreateDirectory(assetsDir);
            var assetPath = Path.Combine(assetsDir, "matrix.bin");
            await File.WriteAllBytesAsync(assetPath, Encoding.UTF8.GetBytes("matrix-bytes"));
            var assetUri = new Uri(assetPath).AbsoluteUri;

            WriteManifest($$"""
                {
                  "name": "Demo",
                  "version": "1.0.0",
                  "dataAssets": [
                    {
                      "id": "matrix",
                      "runtimeIdentifier": "any",
                      "uri": "{{assetUri}}",
                      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                      "size": 12,
                      "extractTo": "."
                    }
                  ]
                }
                """);

            var output = Path.Combine(m_outputDir, "demo.zip");
            await PackRunner.RunAsync(new PackOptions { ModuleDir = m_moduleDir, OutputPath = output });

            Assert.That(File.Exists(output), Is.True);
            using var zip = ZipFile.OpenRead(output);
            Assert.That(zip.Entries.Select(e => e.FullName.Replace('\\', '/')),
                Has.Some.EqualTo("assets/matrix.bin"));
        }

        [Test]
        public void FailsWhenInlineAssetFileMissingTest()
        {
            var missingPath = Path.Combine(m_moduleDir, "assets", "foo.bin");
            var missingUri = new Uri(missingPath).AbsoluteUri;

            WriteManifest($$"""
                {
                  "name": "Demo",
                  "version": "1.0.0",
                  "dataAssets": [
                    {
                      "id": "foo",
                      "runtimeIdentifier": "any",
                      "uri": "{{missingUri}}",
                      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                    }
                  ]
                }
                """);

            Assert.ThrowsAsync<InvalidOperationException>(
                () => PackRunner.RunAsync(new PackOptions { ModuleDir = m_moduleDir }));
        }

        [Test]
        public void RejectsExternalUriByDefaultTest()
        {
            WriteManifest("""
                {
                  "name": "Demo",
                  "version": "1.0.0",
                  "dataAssets": [
                    {
                      "id": "remote",
                      "runtimeIdentifier": "any",
                      "uri": "https://example.com/asset.zip",
                      "sha256": "00"
                    }
                  ]
                }
                """);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => PackRunner.RunAsync(new PackOptions { ModuleDir = m_moduleDir }));
            Assert.That(ex!.Message, Does.Contain("non-file URI"));
        }

        [Test]
        public async Task AcceptsExternalUriWithFlagTest()
        {
            WriteManifest("""
                {
                  "name": "Demo",
                  "version": "1.0.0",
                  "dataAssets": [
                    {
                      "id": "remote",
                      "runtimeIdentifier": "any",
                      "uri": "https://example.com/asset.zip",
                      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                    }
                  ]
                }
                """);

            var output = Path.Combine(m_outputDir, "demo.zip");
            await PackRunner.RunAsync(new PackOptions
            {
                ModuleDir = m_moduleDir,
                OutputPath = output,
                AllowExternalUris = true,
            });

            Assert.That(File.Exists(output), Is.True);
        }

        private void WriteManifest(string json)
        {
            File.WriteAllText(Path.Combine(m_moduleDir, PackRunner.MANIFEST_FILENAME), json, Encoding.UTF8);
        }
    }
}
