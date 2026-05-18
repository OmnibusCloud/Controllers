using OutWit.Controller.Assets.Pack.Csproj;

namespace OutWit.Controller.Assets.Pack.Tests.Csproj
{
    [TestFixture]
    public class ControllerDataAssetEditorTests
    {
        #region Fields

        private string m_tempDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "outwit-editor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempDir))
                Directory.Delete(m_tempDir, recursive: true);
        }

        #endregion

        #region Load

        [Test]
        public void LoadReadsControllerNameVersionAndEntriesTest()
        {
            var path = WriteCsproj(SampleCsproj);

            var editor = ControllerDataAssetEditor.Load(path);

            Assert.That(editor.ControllerName, Is.EqualTo("Render"));
            Assert.That(editor.Version, Is.EqualTo("1.15.1"));
            Assert.That(editor.Entries.Count, Is.EqualTo(2));

            var first = editor.Entries[0];
            Assert.That(first.Id, Is.EqualTo("blender-win-x64"));
            Assert.That(first.RuntimeIdentifier, Is.EqualTo("win-x64"));
            Assert.That(first.Uri, Does.EndWith("/blender-windows-x64.zip"));
            Assert.That(first.Sha256, Has.Length.EqualTo(64));
            Assert.That(first.Size, Is.EqualTo(422542714));
            Assert.That(first.ExtractTo, Is.EqualTo("blender/windows-x64/"));
            Assert.That(first.PackSource, Is.EqualTo("blender/windows-x64"));
            Assert.That(first.PackKind, Is.EqualTo(ControllerDataAssetEntry.PackKindZipFolder));

            var second = editor.Entries[1];
            Assert.That(second.Id, Is.EqualTo("benchmark-scene"));
            Assert.That(second.PackKind, Is.EqualTo(ControllerDataAssetEntry.PackKindSingleFile));
        }

        [Test]
        public void LoadThrowsWhenControllerNameMissingTest()
        {
            var path = WriteCsproj(@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>");

            Assert.That(() => ControllerDataAssetEditor.Load(path),
                        Throws.InstanceOf<InvalidOperationException>().With.Message.Contain("ControllerName"));
        }

        #endregion

        #region Apply

        [Test]
        public void ApplyRewritesShaSizeUriForMatchingAssetTest()
        {
            var path = WriteCsproj(SampleCsproj);
            var editor = ControllerDataAssetEditor.Load(path);

            var updates = new Dictionary<string, AssetUpdate>
            {
                ["blender-win-x64"] = new AssetUpdate(
                    Sha256: new string('a', 64),
                    Size: 999_999_999,
                    Uri: "gh-release://OmnibusCloud/Controllers/render-v2.0.0/blender-windows-x64.zip"),
            };

            var result = editor.Apply(updates);

            Assert.That(result, Does.Contain($"<Sha256>{new string('a', 64)}</Sha256>"));
            Assert.That(result, Does.Contain("<Size>999999999</Size>"));
            Assert.That(result, Does.Contain("render-v2.0.0/blender-windows-x64.zip"));
            // Untouched entry stays the same.
            Assert.That(result, Does.Contain("benchmark-scene"));
        }

        [Test]
        public void ApplyUpdatesTopLevelVersionExactlyOnceTest()
        {
            var path = WriteCsproj(SampleCsproj);
            var editor = ControllerDataAssetEditor.Load(path);

            var result = editor.Apply(new Dictionary<string, AssetUpdate>(), newVersion: "1.16.0");

            // Top-level <Version>1.15.1</Version> bumped, and no extra
            // <Version> tags introduced.
            var versionTagCount = System.Text.RegularExpressions.Regex.Matches(result, "<Version>1.16.0</Version>").Count;
            Assert.That(versionTagCount, Is.EqualTo(1));
            Assert.That(result, Does.Not.Contain("<Version>1.15.1</Version>"));
        }

        [Test]
        public void ApplyPreservesLineEndingsAndIndentationTest()
        {
            var path = WriteCsproj(SampleCsproj);
            var originalText = File.ReadAllText(path);
            var editor = ControllerDataAssetEditor.Load(path);

            var updates = new Dictionary<string, AssetUpdate>
            {
                ["blender-win-x64"] = new AssetUpdate(
                    Sha256: new string('b', 64),
                    Size: 42,
                    Uri: "gh-release://OmnibusCloud/Controllers/render-v1.15.1/blender-windows-x64.zip"),
            };
            var rewritten = editor.Apply(updates);

            // Same number of lines.
            var originalLineCount = originalText.Split('\n').Length;
            var rewrittenLineCount = rewritten.Split('\n').Length;
            Assert.That(rewrittenLineCount, Is.EqualTo(originalLineCount));

            // Same CRLF posture.
            var originalHasCrlf = originalText.Contains("\r\n");
            var rewrittenHasCrlf = rewritten.Contains("\r\n");
            Assert.That(rewrittenHasCrlf, Is.EqualTo(originalHasCrlf));
        }

        [Test]
        public void ApplyNoUpdatesReturnsTextEquivalentToOriginalTest()
        {
            var path = WriteCsproj(SampleCsproj);
            var editor = ControllerDataAssetEditor.Load(path);

            var result = editor.Apply(new Dictionary<string, AssetUpdate>(), newVersion: null);

            Assert.That(result, Is.EqualTo(File.ReadAllText(path)));
        }

        #endregion

        #region Tools

        private string WriteCsproj(string content)
        {
            var path = Path.Combine(m_tempDir, "Sample.csproj");
            File.WriteAllText(path, content);
            return path;
        }

        // Trimmed Render-shape sample with one zip-folder + one single-file entry.
        private const string SampleCsproj = @"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <ControllerName>Render</ControllerName>
    <Version>1.15.1</Version>
  </PropertyGroup>

  <ItemGroup>
    <ControllerDataAsset Include=""blender-win-x64"">
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
      <Uri>gh-release://OmnibusCloud/Controllers/render-v1.15.1/blender-windows-x64.zip</Uri>
      <Sha256>0000000000000000000000000000000000000000000000000000000000000000</Sha256>
      <Size>422542714</Size>
      <ExtractTo>blender/windows-x64/</ExtractTo>
      <PackSource>blender/windows-x64</PackSource>
      <PackKind>zip-folder</PackKind>
    </ControllerDataAsset>
    <ControllerDataAsset Include=""benchmark-scene"">
      <RuntimeIdentifier>any</RuntimeIdentifier>
      <Uri>gh-release://OmnibusCloud/Controllers/render-v1.15.1/benchmark_scene.blend</Uri>
      <Sha256>1111111111111111111111111111111111111111111111111111111111111111</Sha256>
      <Size>124563</Size>
      <ExtractTo>.</ExtractTo>
      <PackSource>benchmark/render/benchmark_scene.blend</PackSource>
      <PackKind>single-file</PackKind>
    </ControllerDataAsset>
  </ItemGroup>

</Project>
";

        #endregion
    }
}
