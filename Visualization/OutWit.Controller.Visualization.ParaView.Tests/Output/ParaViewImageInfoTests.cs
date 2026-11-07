using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;

namespace OutWit.Controller.Visualization.ParaView.Tests.Output;

/// <summary>
/// Header-level image inspection: PNG/JPEG dimensions and alpha without decoding pixels.
/// </summary>
[TestFixture]
public sealed class ParaViewImageInfoTests
{
    #region Tests

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
}
