using System.Diagnostics;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// A case folder is never read through a link: a directory junction (on
/// Windows, no privilege needed) or a symbolic link inside the case is
/// refused by its path, never entered, and a path that crosses one names
/// no case file. A test that cannot make its link says so and is ignored,
/// never passed.
/// </summary>
[TestFixture]
public class FoamCaseFolderRulesTests
{
    private string m_root = null!;

    private string m_case = null!;

    private string m_elsewhere = null!;

    private string? m_link;

    [SetUp]
    public void Setup()
    {
        m_root = OpenFOAMTestPaths.CreateScratch("foam-folder");
        m_case = Path.Combine(m_root, "case");
        m_elsewhere = Path.Combine(m_root, "elsewhere");
        Write(Path.Combine(m_case, "system", "controlDict"), "application simpleFoam;\n");
        Write(Path.Combine(m_case, "0", "U"), "internalField uniform (1 0 0);\n");
        Write(Path.Combine(m_elsewhere, "x"), "not the case's\n");
    }

    [TearDown]
    public void TearDown()
    {
        // The link goes first: it is removed as a link, the folder it points to left as it is.
        if (m_link != null && Directory.Exists(m_link))
            Directory.Delete(m_link, recursive: false);

        OpenFOAMTestPaths.TryDelete(m_root);
    }

    #region Tools

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, text);
    }

    /// <summary>Links a folder to another - a junction on Windows, a symbolic link elsewhere - or ignores the test.</summary>
    private void RequireLink(string link, string target)
    {
        m_link = link;
        Directory.CreateDirectory(Path.GetDirectoryName(link) ?? string.Empty);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                process?.WaitForExit();
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Assert.Ignore($"the link could not be made: {exception.Message}");
        }

        if (!Directory.Exists(link) || new DirectoryInfo(link).LinkTarget == null)
            Assert.Ignore("the link could not be made on this machine");
    }

    #endregion

    #region Folder Tests

    [Test]
    public void ALinkInsideTheCaseIsRefusedByItsPathAndNeverEnteredTest()
    {
        RequireLink(Path.Combine(m_case, "constant", "shared"), m_elsewhere);
        Assert.That(File.Exists(Path.Combine(m_case, "constant", "shared", "x")), Is.True, "the file is reachable through the link");

        var findings = FoamCaseFolderRules.ValidateFolder(m_case);
        var links = new List<string>();
        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "constant/shared: a link (a directory junction or a symbolic link) inside the case; a case travels as the files it holds, never what a link points to - copy the files into the case."
        }));
        Assert.That(links, Is.EqualTo(new[] { "constant/shared" }));
        Assert.That(files.Select(file => file.RelativePath), Is.EqualTo(new[] { "0/U", "system/controlDict" }), "nothing behind the link is a file of the case");
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "constant/shared/x"), Is.True);
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "constant/shared"), Is.True);
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "0/U"), Is.False);
    }

    [Test]
    public void AFolderWithoutALinkHasNoFindingsAndEveryFileIsListedWithItsSizeTest()
    {
        var links = new List<string>();

        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(FoamCaseFolderRules.ValidateFolder(m_case), Is.Empty);
        Assert.That(links, Is.Empty);
        Assert.That(files, Is.EqualTo(new[] { ("0/U", 31L), ("system/controlDict", 24L) }));
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "system/controlDict"), Is.False);
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "system/missing"), Is.False, "a path that does not exist crosses nothing");
    }

    #endregion
}
