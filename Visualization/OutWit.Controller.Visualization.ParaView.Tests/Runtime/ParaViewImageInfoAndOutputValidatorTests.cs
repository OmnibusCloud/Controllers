using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// Header-level image inspection and the output validation rules of docs 03, section 12.
/// </summary>
[TestFixture]
public sealed class ParaViewImageInfoAndOutputValidatorTests
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

    #region ImageInfo

    [Test]
    public void PngHeaderIsReadTest()
    {
        var info = ParaViewImageInfo.TryRead(new MemoryStream(TestImages.Png(37, 21, alpha: false)));
        var alpha = ParaViewImageInfo.TryRead(new MemoryStream(TestImages.Png(5, 6, alpha: true)));

        Assert.Multiple(() =>
        {
            Assert.That(info, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 37, 21, false)));
            Assert.That(alpha, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 5, 6, true)));
        });
    }

    [Test]
    public void JpegHeaderIsReadTest()
    {
        var info = ParaViewImageInfo.TryRead(new MemoryStream(TestImages.JpegHeaderOnly(640, 480)));

        Assert.That(info, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Jpeg, 640, 480, false)));
    }

    [Test]
    public void UnknownSignaturesAreNullTest()
    {
        Assert.That(ParaViewImageInfo.TryRead(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])), Is.Null);
        Assert.That(ParaViewImageInfo.TryRead(new MemoryStream([])), Is.Null);
        Assert.That(ParaViewImageInfo.TryRead(new MemoryStream("GIF89a......................"u8.ToArray())), Is.Null);
    }

    #endregion

    #region Output validation

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
