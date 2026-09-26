using System.Text;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamCaseTextTests
{
    #region View Tests

    [Test]
    public void EveryByteSurvivesTheRoundTripTest()
    {
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        var text = FoamCaseText.FromBytes(bytes);

        Assert.That(text, Has.Length.EqualTo(bytes.Length), "one character per byte");
        Assert.That(FoamCaseText.ToBytes(text), Is.EqualTo(bytes));
    }

    [Test]
    public void ABomAndAnInvalidSequenceSurviveTheRoundTripTest()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'n', (byte)'u', 0x20, 0xC3, 0x28, 0xE9, (byte)';', 0x0D, 0x0A];

        Assert.That(FoamCaseText.ToBytes(FoamCaseText.FromBytes(bytes)), Is.EqualTo(bytes));
    }

    [Test]
    public void ACharacterBeyondOneByteIsRefusedNotReplacedTest()
    {
        Assert.Throws<EncoderFallbackException>(() => FoamCaseText.ToBytes("nu €;"));
    }

    #endregion

    #region Value Tests

    [Test]
    public void AnAsciiValueIsItselfTest()
    {
        Assert.That(FoamCaseText.FromValue("1.5e-05"), Is.EqualTo("1.5e-05"));
    }

    [Test]
    public void AValueEntersTheViewAsItsUtf8BytesTest()
    {
        var view = FoamCaseText.FromValue("été");

        Assert.That(FoamCaseText.ToBytes(view), Is.EqualTo(Encoding.UTF8.GetBytes("été")));
    }

    #endregion
}
