using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

[TestFixture]
public sealed class ParaViewLogicalPathTests
{
    [TestCase("data/field.vtu")]
    [TestCase("state-inputs/series_000.vtu")]
    [TestCase("a/b/c/d.pvd")]
    [TestCase("mesh.vtk")]
    [TestCase("data/with space/file name.vti")]
    public void AdmissiblePathsPassTest(string path)
    {
        Assert.That(ParaViewLogicalPath.Check(path), Is.Null);
    }

    [TestCase("", "empty")]
    [TestCase("/etc/passwd", "absolute")]
    [TestCase("C:/data/file.vtu", "drive letter")]
    [TestCase("c:\\data\\file.vtu", "separators")]
    [TestCase("\\\\server\\share\\file.vtu", "separators")]
    [TestCase("data\\file.vtu", "separators")]
    [TestCase("../data/file.vtu", "traverses")]
    [TestCase("data/../../file.vtu", "traverses")]
    [TestCase("data/./file.vtu", "'.' segment")]
    [TestCase("data//file.vtu", "empty segment")]
    [TestCase("data/", "empty segment")]
    [TestCase("file:///data/file.vtu", "URI")]
    [TestCase("https://example.org/file.vtu", "URI")]
    [TestCase("data/file.vtu ", "space or dot")]
    [TestCase("data/file.", "space or dot")]
    [TestCase("data/fi|le.vtu", "not portable")]
    [TestCase("data/fi:le.vtu", "not portable")]
    [TestCase("data/file\u0001.vtu", "control")]
    public void InadmissiblePathsAreRejectedTest(string path, string reason)
    {
        var violation = ParaViewLogicalPath.Check(path);
        Assert.That(violation, Is.Not.Null);
        Assert.That(violation, Does.Contain(reason));
    }

    [Test]
    public void OverlongPathIsRejectedTest()
    {
        var path = "d/" + new string('x', ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS);
        Assert.That(ParaViewLogicalPath.Check(path), Does.Contain("exceeds"));
    }

    [Test]
    public void ResolveUnderStaysInsideTheRootTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv_root_" + Guid.NewGuid().ToString("N"));
        var resolved = ParaViewLogicalPath.ResolveUnder(root, "data/sub/file.vtu");

        Assert.That(resolved, Does.StartWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar));
        Assert.That(resolved, Does.EndWith(Path.Combine("data", "sub", "file.vtu")));
    }

    [TestCase("../escape.vtu")]
    [TestCase("/abs.vtu")]
    [TestCase("C:/abs.vtu")]
    public void ResolveUnderRefusesEscapesTest(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "pv_root_" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => ParaViewLogicalPath.ResolveUnder(root, path));
    }

    [TestCase("data/file.vtu", true)]
    [TestCase("C:\\x", true)]
    [TestCase("D:file", true)]
    [TestCase("http://x", true)]
    [TestCase("file.vtu", false)]
    [TestCase("Surface", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void LooksLikePathHeuristicTest(string? value, bool expected)
    {
        Assert.That(ParaViewLogicalPath.LooksLikePath(value), Is.EqualTo(expected));
    }
}
