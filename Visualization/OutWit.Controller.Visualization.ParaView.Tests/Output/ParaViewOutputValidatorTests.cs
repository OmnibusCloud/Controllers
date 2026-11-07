using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;

namespace OutWit.Controller.Visualization.ParaView.Tests.Output;

/// <summary>
/// The output validation rules of docs 03, section 12.
/// </summary>
[TestFixture]
public sealed class ParaViewOutputValidatorTests
{
    #region Fields

    private string m_dir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_dir = Path.Combine(Path.GetTempPath(), $"pv_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_dir))
            Directory.Delete(m_dir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void ValidOutputPassesTest()
    {
        var path = Write("frame.png", TestImages.Png(64, 32, alpha: false));

        var (info, size) = ParaViewOutputValidator.Validate(path, m_dir, ParaViewImageFormat.Png, 64, 32, transparentBackground: false);

        Assert.That(info.Width, Is.EqualTo(64));
        Assert.That(size, Is.GreaterThan(0));
    }

    [Test]
    public void WrongDimensionsAreRejectedTest()
    {
        var path = Write("frame.png", TestImages.Png(65, 32, alpha: false));

        var exception = Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(path, m_dir, ParaViewImageFormat.Png, 64, 32, false));
        Assert.That(exception!.Message, Does.Contain("65x32"));
    }

    [Test]
    public void WrongFormatIsRejectedTest()
    {
        var path = Write("frame.jpg", TestImages.JpegHeaderOnly(64, 32));

        var exception = Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(path, m_dir, ParaViewImageFormat.Png, 64, 32, false));
        Assert.That(exception!.Message, Does.Contain("signature is Jpeg"));
    }

    [Test]
    public void MissingAlphaWhenTransparencyRequestedIsRejectedTest()
    {
        var path = Write("frame.png", TestImages.Png(8, 8, alpha: false));

        var exception = Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(path, m_dir, ParaViewImageFormat.Png, 8, 8, transparentBackground: true));
        Assert.That(exception!.Message, Does.Contain("alpha"));
    }

    [Test]
    public void EmptyMissingAndGarbageOutputsAreRejectedTest()
    {
        var empty = Write("empty.png", []);
        Assert.That(Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(empty, m_dir, ParaViewImageFormat.Png, 1, 1, false))!.Message, Does.Contain("empty"));
        File.Delete(empty);

        Assert.That(Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(Path.Combine(m_dir, "nope.png"), m_dir, ParaViewImageFormat.Png, 1, 1, false))!.Message, Does.Contain("no output file"));

        var garbage = Write("garbage.png", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        Assert.That(Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(garbage, m_dir, ParaViewImageFormat.Png, 1, 1, false))!.Message, Does.Contain("not a recognizable"));
    }

    [Test]
    public void ExtraFilesInTheOutputDirectoryAreRejectedTest()
    {
        var path = Write("frame.png", TestImages.Png(4, 4, alpha: false));
        Write("stray.txt", [1]);

        var exception = Assert.Throws<InvalidOperationException>(() => ParaViewOutputValidator.Validate(path, m_dir, ParaViewImageFormat.Png, 4, 4, false));
        Assert.That(exception!.Message, Does.Contain("stray.txt"));
    }

    #endregion

    #region Tools

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(m_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    #endregion
}
