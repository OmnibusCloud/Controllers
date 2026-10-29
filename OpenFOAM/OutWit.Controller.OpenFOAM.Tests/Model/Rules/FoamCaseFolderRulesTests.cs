using System.Diagnostics;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// A link inside a case folder is the file or folder it points to when that
/// lies inside the case (OpenFOAM's tutorials keep such links), and is
/// refused by its path, never entered, when it points outside; a path that
/// crosses an outside link names no case file. Folder links are junctions
/// on Windows (no privilege needed) and symbolic links elsewhere; a file
/// link is a symbolic link where the account may create one. A test that
/// cannot make its link says so and is ignored, never passed.
/// </summary>
[TestFixture]
public class FoamCaseFolderRulesTests
{
    private string m_root = null!;

    private string m_case = null!;

    private string m_elsewhere = null!;

    private readonly List<string> m_links = [];

    [SetUp]
    public void Setup()
    {
        m_root = OpenFOAMTestPaths.CreateScratch("foam-folder");
        m_case = Path.Combine(m_root, "case");
        m_elsewhere = Path.Combine(m_root, "elsewhere");
        Write(Path.Combine(m_case, "system", "controlDict"), "application simpleFoam;\n");
        Write(Path.Combine(m_case, "system", "fvSolution"), "solvers { }\n");
        Write(Path.Combine(m_case, "0", "U"), "internalField uniform (1 0 0);\n");
        Write(Path.Combine(m_elsewhere, "x"), "not the case's\n");
    }

    [TearDown]
    public void TearDown()
    {
        // The links go first, each removed as a link, what it points to left as it is.
        foreach (var link in m_links)
        {
            if (Directory.Exists(link))
                Directory.Delete(link, recursive: false);
            else if (File.Exists(link))
                File.Delete(link);
        }

        m_links.Clear();
        OpenFOAMTestPaths.TryDelete(m_root);
    }

    #region Tools

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, text);
    }

    /// <summary>Links a folder to another - a junction on Windows, a symbolic link elsewhere - or ignores the test.</summary>
    private void RequireFolderLink(string link, string target)
    {
        m_links.Add(link);
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
            Assert.Ignore($"the folder link could not be made: {exception.Message}");
        }

        if (!Directory.Exists(link) || new DirectoryInfo(link).LinkTarget == null)
            Assert.Ignore("the folder link could not be made on this machine");
    }

    /// <summary>Links a file to another by a symbolic link, relative as the tutorials write them, or ignores the test.</summary>
    private void RequireFileLink(string link, string relativeTarget)
    {
        m_links.Add(link);
        Directory.CreateDirectory(Path.GetDirectoryName(link) ?? string.Empty);
        try
        {
            File.CreateSymbolicLink(link, relativeTarget);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"the file link could not be made (a symbolic link needs a privilege on Windows): {exception.Message}");
        }

        if (!File.Exists(link) || new FileInfo(link).LinkTarget == null)
            Assert.Ignore("the file link could not be made on this machine");
    }

    #endregion

    #region Folder Tests

    [Test]
    public void AFolderLinkInsideTheCaseIsWalkedAsTheFolderItPointsToTest()
    {
        // A multi-region case: system/region is a link to a folder of the case.
        RequireFolderLink(Path.Combine(m_case, "system", "region"), Path.Combine(m_case, "0"));

        var links = new List<string>();
        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(FoamCaseFolderRules.ValidateFolder(m_case), Is.Empty);
        Assert.That(links, Is.Empty);
        Assert.That(files, Is.EqualTo(new[] { ("0/U", 31L), ("system/controlDict", 24L), ("system/fvSolution", 12L), ("system/region/U", 31L) }), "the linked folder's files are the link's, where OpenFOAM finds them");
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "system/region/U"), Is.False);
    }

    [Test]
    public void AFileLinkInsideTheCaseIsTheFileItPointsToTest()
    {
        // The tutorials' shape: system/<region>/fvSolution -> ../fvSolution.
        RequireFileLink(Path.Combine(m_case, "system", "region", "fvSolution"), Path.Combine("..", "fvSolution"));

        var links = new List<string>();
        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(FoamCaseFolderRules.ValidateFolder(m_case), Is.Empty);
        Assert.That(links, Is.Empty);
        Assert.That(files, Is.EqualTo(new[] { ("0/U", 31L), ("system/controlDict", 24L), ("system/fvSolution", 12L), ("system/region/fvSolution", 12L) }), "the link is listed as the file, with the target's size");
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "system/region/fvSolution"), Is.False);
    }

    [Test]
    public void ALinkThatPointsOutsideTheCaseIsRefusedByItsPathAndNeverEnteredTest()
    {
        RequireFolderLink(Path.Combine(m_case, "constant", "shared"), m_elsewhere);
        Assert.That(File.Exists(Path.Combine(m_case, "constant", "shared", "x")), Is.True, "the file is reachable through the link");

        var findings = FoamCaseFolderRules.ValidateFolder(m_case);
        var links = new List<string>();
        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "constant/shared: a link (a directory junction or a symbolic link) that points outside the case; a case travels as the files it holds, never what a link points to - copy the files into the case."
        }));
        Assert.That(links, Is.EqualTo(new[] { "constant/shared" }));
        Assert.That(files.Select(file => file.RelativePath), Is.EqualTo(new[] { "0/U", "system/controlDict", "system/fvSolution" }), "nothing behind the link is a file of the case");
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "constant/shared/x"), Is.True);
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "constant/shared"), Is.True);
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "0/U"), Is.False);
    }

    [Test]
    public void AFolderLinkBackToTheCaseItselfIsInsideAndListsNothingTwiceTest()
    {
        RequireFolderLink(Path.Combine(m_case, "constant", "up"), m_case);

        var links = new List<string>();
        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(FoamCaseFolderRules.ValidateFolder(m_case), Is.Empty);
        Assert.That(links, Is.Empty);
        Assert.That(files.Select(file => file.RelativePath), Is.EqualTo(new[] { "0/U", "system/controlDict", "system/fvSolution" }), "every file once, whatever loops back to the root");
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "constant/up/system/controlDict"), Is.False);
    }

    [Test]
    public void AFolderWithoutALinkHasNoFindingsAndEveryFileIsListedWithItsSizeTest()
    {
        var links = new List<string>();

        var files = FoamCaseFolderRules.EnumerateFiles(m_case, links);

        Assert.That(FoamCaseFolderRules.ValidateFolder(m_case), Is.Empty);
        Assert.That(links, Is.Empty);
        Assert.That(files, Is.EqualTo(new[] { ("0/U", 31L), ("system/controlDict", 24L), ("system/fvSolution", 12L) }));
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "system/controlDict"), Is.False);
        Assert.That(FoamCaseFolderRules.CrossesLink(m_case, "system/missing"), Is.False, "a path that does not exist crosses nothing");
    }

    #endregion
}
