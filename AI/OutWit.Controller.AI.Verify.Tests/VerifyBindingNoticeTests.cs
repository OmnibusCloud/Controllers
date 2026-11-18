using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests;

/// <summary>
/// The one assembly of the module that is another's work, <c>Wasmtime.Dotnet.dll</c>, and what
/// the module's third-party notices say of it (audit finding V:M-9). The notices state the
/// package, its authors, the file's SHA-256, the commit of its sources and its licence; every
/// one of those is held here to the package the build restored and to the file the module
/// carries, so a reference that moves without its notice fails by the line that is no longer true.
/// <para>
/// The Corpus controller's module carries the same file. Its notices write the same four lines
/// and the same licence text: the lines are built here in that form, and the text is held to the
/// SHA-256 both controllers pin.
/// </para>
/// </summary>
[TestFixture]
public sealed class VerifyBindingNoticeTests
{
    #region Constants

    private const string NOTICES = "THIRD-PARTY-NOTICES.txt";
    private const string PROJECT_FOLDER = "OutWit.Controller.AI.Verify";
    private const string ASSEMBLY = "Wasmtime.Dotnet.dll";
    private const string PACKAGE_FOLDER_METADATA = "WasmtimePackageFolder";
    private const string NUSPEC = "wasmtime.nuspec";

    // The licence file of the binding's repository at the commit the package names, by its
    // SHA-256: the value this controller's notices table and the Corpus controller's notices
    // (Notices/CorpusNotices.targets, TextSha256) both pin.
    private const string LICENCE_TEXT_SHA256 = "a6c48161a09acc75a0e25503bab66a731eb5fba5392ed4bb4743e4ba5085327a";
    private const string TEXT_MARKER = "(SHA-256 of that file: " + LICENCE_TEXT_SHA256 + ")\n\n";

    private const string CORPUS_FOLDER = "OutWit.Controller.AI.Corpus";
    private const string CORPUS_NOTICES = "CorpusNotices.targets";
    private const string CORPUS_COMPONENT = "Wasmtime.Dotnet";

    #endregion

    #region Tests

    [Test]
    public void M9TheBindingsNoticeStatesWhatThePackageStatesTest()
    {
        var package = Package();
        var notices = Notices();

        Assert.Multiple(() =>
        {
            Assert.That(package.Version, Is.EqualTo(WasmHostBinding.BUILT_AGAINST), "the package the tests restored is the binding the host is built against");

            foreach (var line in new[]
                     {
                         $"Package:   {package.Id} {package.Version}, by {package.Authors}",
                         $"File:      {ASSEMBLY}, as the package ships it (SHA-256 {Sha256(Path.Combine(StagedModule(), ASSEMBLY))})",
                         $"Source:    {package.Source} at {package.Commit}",
                         $"Licence:   {package.Licence}; the text below is the file LICENSE of that repository at that commit"
                     })
            {
                Assert.That(notices, Does.Contain("\n" + line + "\n"),
                    "the module's notices do not carry this line, which is what the restored package and the staged file state. " +
                    "When the Wasmtime reference moves, the notices table (toolchain/notices/notices.json) moves with it");
            }

            Assert.That(notices, Does.Contain($"/{package.Commit}/LICENSE"), "the licence text is taken at the commit the package names");
        });
    }

    [Test]
    public void M9TheBindingInTheModuleIsThePackagesFileTest()
    {
        var staged = Sha256(Path.Combine(StagedModule(), ASSEMBLY));
        var shipped = Directory.GetFiles(Path.Combine(PackageFolder(), "lib"), ASSEMBLY, SearchOption.AllDirectories).Select(Sha256).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shipped, Is.Not.Empty, "the package carries the assembly");
            Assert.That(shipped, Does.Contain(staged), $"the {ASSEMBLY} in the module is not, byte for byte, a file of the package: the build resolved another version, or the file was changed");
        });
    }

    [Test]
    public void M9TheBindingsLicenceTextIsTheOneBothControllersPinTest()
    {
        var notices = Notices();
        var at = notices.IndexOf(TEXT_MARKER, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThan(0), "the notices give the licence text with the SHA-256 of the file it is");

        // The binding is the last part of the file: its text runs to the end, before the line
        // that closes the part.
        var text = notices[(at + TEXT_MARKER.Length)..^1];

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), Is.EqualTo(LICENCE_TEXT_SHA256),
                "the text printed in the notices is not the file its SHA-256 names");

            // Where the Corpus controller's notices are in the tree, they name the same package,
            // the same commit and the same text.
            var corpus = Path.Combine(SolutionRoot(), "AI", CORPUS_FOLDER, "Notices", CORPUS_NOTICES);
            if (!File.Exists(corpus))
                return;

            var component = XDocument.Load(corpus).Descendants().Single(element => element.Name.LocalName == "CorpusNoticeComponent" && (string?)element.Attribute("Include") == CORPUS_COMPONENT);
            var package = Package();

            Assert.That(Value(component, "TextSha256"), Is.EqualTo(LICENCE_TEXT_SHA256), "Corpus pins another licence text for the same assembly");
            Assert.That(Value(component, "Version"), Is.EqualTo(package.Version), "Corpus's notices name another version of the binding");
            Assert.That(Value(component, "Commit"), Is.EqualTo(package.Commit), "Corpus's notices name another commit of the binding's sources");
            Assert.That(Value(component, "License"), Is.EqualTo(package.Licence), "Corpus's notices name another licence for the binding");
        });
    }

    #endregion

    #region Tools

    /// <summary>What the restored package's own description states.</summary>
    private static (string Id, string Version, string Authors, string Licence, string Source, string Commit) Package()
    {
        var path = Path.Combine(PackageFolder(), NUSPEC);
        Assert.That(File.Exists(path), Is.True, $"the package's description at '{path}'");

        var metadata = XDocument.Load(path).Descendants().Single(element => element.Name.LocalName == "metadata");
        var repository = metadata.Elements().Single(element => element.Name.LocalName == "repository");
        var licence = metadata.Elements().Single(element => element.Name.LocalName == "license" && (string?)element.Attribute("type") == "expression");

        return (Value(metadata, "id"), Value(metadata, "version"), Value(metadata, "authors"), licence.Value.Trim(),
            (string?)repository.Attribute("url") ?? "", (string?)repository.Attribute("commit") ?? "");
    }

    private static string Value(XElement parent, string name)
    {
        return parent.Elements().Single(element => element.Name.LocalName == name).Value.Trim();
    }

    /// <summary>The folder the build restored the Wasmtime package into (the test project records it at build time).</summary>
    private static string PackageFolder()
    {
        var folder = typeof(VerifyBindingNoticeTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == PACKAGE_FOLDER_METADATA)?.Value;

        Assert.That(folder, Is.Not.Null.And.Not.Empty, "the test project records where the Wasmtime package was restored");
        Assert.That(Directory.Exists(folder), Is.True, $"the restored package at '{folder}'");
        return folder!;
    }

    /// <summary>The committed notices of the module, with line feeds whatever the checkout.</summary>
    private static string Notices()
    {
        return File.ReadAllText(Path.Combine(SolutionRoot(), "AI", PROJECT_FOLDER, NOTICES)).Replace("\r\n", "\n");
    }

    private static string StagedModule()
    {
        var staged = VerifyRigModules.StagedRoot();
        var module = staged == null ? null : Path.Combine(staged, VerifyRigModules.VERIFY);
        if (module == null || !File.Exists(Path.Combine(module, ASSEMBLY)))
            VerifyRigModules.Missing($"no staged module with {ASSEMBLY} at '{module}'");

        return module;
    }

    private static string SolutionRoot()
    {
        var root = VerifyRigModules.SolutionRoot();
        if (root == null)
            VerifyRigModules.Missing("Solution root not found");

        return root;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    #endregion
}
