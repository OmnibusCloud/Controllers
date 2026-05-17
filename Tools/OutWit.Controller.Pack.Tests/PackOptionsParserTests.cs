using OutWit.Controller.Pack.Options;

namespace OutWit.Controller.Pack.Tests
{
    [TestFixture]
    public class PackOptionsParserTests
    {
        [Test]
        public void NoArgsShowsHelpTest()
        {
            Assert.That(PackOptionsParser.Parse(Array.Empty<string>()).ShowHelp, Is.True);
        }

        [Test]
        public void HelpFlagShowsHelpTest()
        {
            Assert.That(PackOptionsParser.Parse(new[] { "-h" }).ShowHelp, Is.True);
            Assert.That(PackOptionsParser.Parse(new[] { "--help" }).ShowHelp, Is.True);
        }

        [Test]
        public void AcceptsPositionalModuleDirTest()
        {
            var opts = PackOptionsParser.Parse(new[] { "./bin/Release/x.module" });
            Assert.That(opts.ShowHelp, Is.False);
            Assert.That(opts.ModuleDir, Is.EqualTo("./bin/Release/x.module"));
        }

        [Test]
        public void AcceptsModuleFlagTest()
        {
            Assert.That(PackOptionsParser.Parse(new[] { "--module", "./mod" }).ModuleDir, Is.EqualTo("./mod"));
        }

        [Test]
        public void RequiresModuleDirTest()
        {
            Assert.Throws<ArgumentException>(() => PackOptionsParser.Parse(new[] { "--output", "x.zip" }));
        }

        [Test]
        public void ParsesOutputPathTest()
        {
            Assert.That(PackOptionsParser.Parse(new[] { "./mod", "--output", "dist/foo.zip" }).OutputPath,
                Is.EqualTo("dist/foo.zip"));
        }

        [Test]
        public void OutputPathNullWhenOmittedTest()
        {
            Assert.That(PackOptionsParser.Parse(new[] { "./mod" }).OutputPath, Is.Null);
        }

        [Test]
        public void AllowExternalUrisDefaultsFalseTest()
        {
            Assert.That(PackOptionsParser.Parse(new[] { "./mod" }).AllowExternalUris, Is.False);
        }

        [Test]
        public void AllowExternalUrisFlagSetTest()
        {
            Assert.That(PackOptionsParser.Parse(new[] { "./mod", "--allow-external-uris" }).AllowExternalUris, Is.True);
        }

        [Test]
        public void UnknownFlagThrowsTest()
        {
            Assert.Throws<ArgumentException>(() => PackOptionsParser.Parse(new[] { "./mod", "--wat" }));
        }

        [Test]
        public void FlagWithoutValueThrowsTest()
        {
            Assert.Throws<ArgumentException>(() => PackOptionsParser.Parse(new[] { "--module" }));
        }
    }
}
