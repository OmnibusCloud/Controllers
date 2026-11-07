using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

/// <summary>
/// The version-1 admission rules of a data scene: exactly one CalculiX result on a logical path,
/// bounded colour choices, allowlisted presets, defined enums.
/// </summary>
[TestFixture]
public sealed class ParaViewDataSceneValidatorTests
{
    #region Tests

    [Test]
    public void TypicalDataSceneIsAdmissibleTest()
    {
        var errors = Validate(Typical());
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void DataSceneWithoutDigestIsAdmissibleTest()
    {
        var data = Typical();
        data.Attachments[0].Sha256 = string.Empty;
        data.Attachments[0].Size = 0;

        Assert.That(Validate(data), Is.Empty, "the composer stamps digest and size");
    }

    [Test]
    public void EmptyColorArrayAndPresetAreAdmissibleTest()
    {
        var data = Typical();
        data.ColorArrayName = string.Empty;
        data.ColormapPreset = string.Empty;

        Assert.That(Validate(data), Is.Empty);
    }

    [Test]
    public void NoAttachmentIsRejectedTest()
    {
        var data = Typical();
        data.Attachments.Clear();

        Assert.That(Validate(data), Has.Some.Contains("exactly one attachment"));
    }

    [Test]
    public void TwoAttachmentsAreRejectedTest()
    {
        var data = Typical();
        data.Attachments.Add(Attachment("data/other.frd"));

        Assert.That(Validate(data), Has.Some.Contains("exactly one attachment"));
    }

    [TestCase("C:/results/model.frd")]
    [TestCase("/tmp/model.frd")]
    [TestCase("../model.frd")]
    [TestCase("data\\model.frd")]
    [TestCase("")]
    public void ClientPathsAreRejectedTest(string logicalPath)
    {
        var data = Typical();
        data.Attachments[0].LogicalPath = logicalPath;

        Assert.That(Validate(data), Has.Some.StartsWith("attachment:"));
    }

    [TestCase("data/model.vtu")]
    [TestCase("data/model.inp")]
    [TestCase("data/model")]
    public void NonFrdDataIsRejectedTest(string logicalPath)
    {
        var data = Typical();
        data.Attachments[0].LogicalPath = logicalPath;

        Assert.That(Validate(data), Has.Some.Contains("composes .frd only"));
    }

    [Test]
    public void UpperCaseFrdExtensionIsAcceptedTest()
    {
        var data = Typical();
        data.Attachments[0].LogicalPath = "data/MODEL.FRD";

        Assert.That(Validate(data), Is.Empty);
    }

    [Test]
    public void EmptyBlobIdIsRejectedTest()
    {
        var data = Typical();
        data.Attachments[0].BlobId = Guid.Empty;

        Assert.That(Validate(data), Has.Some.Contains("no blob id"));
    }

    [Test]
    public void MalformedDigestIsRejectedTest()
    {
        var data = Typical();
        data.Attachments[0].Sha256 = "not-a-digest";

        Assert.That(Validate(data), Has.Some.Contains("malformed SHA-256"));
    }

    [Test]
    public void NegativeSizeIsRejectedTest()
    {
        var data = Typical();
        data.Attachments[0].Size = -1;

        Assert.That(Validate(data), Has.Some.Contains("negative size"));
    }

    [Test]
    public void SeriesAttachmentIsRejectedTest()
    {
        var data = Typical();
        data.Attachments[0].SeriesGroup = "series";
        data.Attachments[0].TimestepIndices = [0];

        Assert.That(Validate(data), Has.Some.Contains("timestep-independent"));
    }

    [Test]
    public void AuxiliaryRoleIsRejectedTest()
    {
        var data = Typical();
        data.Attachments[0].Role = ParaViewAttachmentRole.Auxiliary;

        Assert.That(Validate(data), Has.Some.Contains("reader input"));
    }

    [Test]
    public void OverlongArrayNameIsRejectedTest()
    {
        var data = Typical();
        data.ColorArrayName = new string('N', ParaViewDataSceneValidator.MAX_ARRAY_NAME_CHARS + 1);

        Assert.That(Validate(data), Has.Some.Contains("exceeds"));
    }

    [Test]
    public void ControlCharactersInArrayNameAreRejectedTest()
    {
        var data = Typical();
        data.ColorArrayName = "ND\nTEMP";

        Assert.That(Validate(data), Has.Some.Contains("control characters"));
    }

    [TestCase(-2)]
    [TestCase(ParaViewDataSceneValidator.MAX_COMPONENT_INDEX + 1)]
    public void ComponentOutsideTheRangeIsRejectedTest(int component)
    {
        var data = Typical();
        data.ColorComponent = component;

        Assert.That(Validate(data), Has.Some.Contains("colour component"));
    }

    [Test]
    public void UnknownPresetIsRejectedTest()
    {
        var data = Typical();
        data.ColormapPreset = "Rainbow of Doom";

        Assert.That(Validate(data), Has.Some.Contains("not allowlisted"));
    }

    [Test]
    public void EveryAllowlistedPresetIsAcceptedTest()
    {
        foreach (var preset in ParaViewDataSceneValidator.COLORMAP_PRESETS)
        {
            var data = Typical();
            data.ColormapPreset = preset;
            Assert.That(Validate(data), Is.Empty, preset);
        }
    }

    [Test]
    public void UndefinedEnumsAreRejectedTest()
    {
        var data = Typical();
        data.ColorAssociation = (ParaViewColorAssociation)7;
        data.Representation = (ParaViewSceneRepresentation)7;
        data.CameraDirection = (ParaViewCameraDirection)70;
        data.FitTo = (ParaViewCameraFit)7;

        var errors = Validate(data);
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Some.Contains("colour association"));
            Assert.That(errors, Has.Some.Contains("representation"));
            Assert.That(errors, Has.Some.Contains("camera direction"));
            Assert.That(errors, Has.Some.Contains("camera fit"));
        });
    }

    #endregion

    #region Tools

    public static ParaViewDataSceneData Typical()
    {
        return new ParaViewDataSceneData
        {
            Attachments = [Attachment("data/model.frd")],
            ColorArrayName = "NDTEMP",
            ColormapPreset = "Cool to Warm"
        };
    }

    public static ParaViewAttachmentRefData Attachment(string logicalPath)
    {
        return new ParaViewAttachmentRefData
        {
            BlobId = Guid.NewGuid(),
            LogicalPath = logicalPath,
            Sha256 = new string('a', 64),
            Size = 4644,
            Role = ParaViewAttachmentRole.ReaderInput
        };
    }

    private static List<string> Validate(ParaViewDataSceneData data)
    {
        var errors = new List<string>();
        ParaViewDataSceneValidator.Validate(data, errors);
        return errors;
    }

    #endregion
}
