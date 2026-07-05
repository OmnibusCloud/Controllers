using OutWit.Controller.Render.Dcc.Services;

namespace OutWit.Controller.Render.Dcc.Tests.Services;

[TestFixture]
public sealed class DccBlenderPythonFormatterTests
{
    #region Tests

    [Test]
    public void FormatDoubleFormatsFiniteValuesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DccBlenderPythonFormatter.FormatDouble(0d), Is.EqualTo("0.0"));
            Assert.That(DccBlenderPythonFormatter.FormatDouble(1.5d), Is.EqualTo("1.5"));
            Assert.That(DccBlenderPythonFormatter.FormatDouble(-0.25d), Is.EqualTo("-0.25"));
        });
    }

    [Test]
    public void FormatDoubleThrowsOnNaNTest()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DccBlenderPythonFormatter.FormatDouble(double.NaN));

        Assert.That(exception!.Message, Does.Contain("non-finite"));
    }

    [Test]
    public void FormatDoubleThrowsOnInfinityTest()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => DccBlenderPythonFormatter.FormatDouble(double.PositiveInfinity));
            Assert.Throws<InvalidOperationException>(() => DccBlenderPythonFormatter.FormatDouble(double.NegativeInfinity));
        });
    }

    #endregion
}
