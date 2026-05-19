using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Controller.Render.Dcc.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Tests.Services;

[TestFixture]
public sealed class DccSceneContractValidatorTests
{
    #region Tests

    [Test]
    public void ValidateValidSceneDoesNotThrowTest()
    {
        var scene = DccRenderTestData.CreateValidScene();

        Assert.DoesNotThrow(() => DccSceneContractValidator.Validate(scene));
    }

    [Test]
    public void ValidateRejectsDuplicateMeshIdsTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Meshes.Add((DccMeshData)scene.Meshes[0].Clone());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("unique mesh ids"));
    }

    [Test]
    public void ValidateRejectsUnsupportedAxisSystemHandednessTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.AxisSystem.Handedness = "diagonal";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("Handedness to be either 'left' or 'right'"));
    }

    [Test]
    public void ValidateRejectsMissingSourceApplicationVersionTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.SourceApplication.ApplicationVersion = string.Empty;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("ApplicationVersion and SourceApplication.ExporterVersion"));
    }

    [Test]
    public void ValidateRejectsMissingSourceExporterVersionTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.SourceApplication.ExporterVersion = string.Empty;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("ApplicationVersion and SourceApplication.ExporterVersion"));
    }

    [Test]
    public void ValidateRejectsUnsupportedAxisSystemAxisNameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.AxisSystem.UpAxis = "W";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("UpAxis and ForwardAxis to use only X, Y, or Z"));
    }

    [Test]
    public void ValidateRejectsDuplicateAxisSystemAxesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.AxisSystem.ForwardAxis = "Z";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("UpAxis and ForwardAxis to be different"));
    }

    [Test]
    public void ValidateRejectsInvalidRenderSettingsTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.RenderSettings.FrameEnd = 0;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("valid render settings"));
    }

    [Test]
    public void ValidateRejectsOutOfRangePerspectiveCameraFovTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Cameras[^1].VerticalFovDegrees = 181d;
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("VerticalFovDegrees in the (0, 180] range"));
    }

    [Test]
    public void ValidateRejectsUnsupportedRenderTargetEngineTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.RenderSettings.TargetEngine = (OutWit.Controller.Render.Model.RenderEngine)999;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("supported render TargetEngine"));
    }

    [Test]
    public void ValidateRejectsMissingTextureImageAssetTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots[0].ImageAssetId = "image:missing";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("missing image asset"));
    }

    [Test]
    public void ValidateRejectsDuplicateTextureSlotKindsTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.BaseColor,
            ImageAssetId = "image:albedo"
        });

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate texture slot kind"));
    }

    [Test]
    public void ValidateRejectsUnsupportedTextureSlotKindTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots[0].Slot = (DccTextureSlotKind)999;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("unsupported texture slot kind"));
    }

    [Test]
    public void ValidateRejectsUnsupportedMaterialKindTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].Kind = (DccMaterialKind)999;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("unsupported material kind"));
    }

    [Test]
    public void ValidateRejectsUnsupportedMaterialAlphaModeTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = (DccMaterialAlphaMode)999;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("unsupported alpha mode"));
    }

    [Test]
    public void ValidateRejectsMaterialBaseColorAlphaTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].BaseColor.A = 0.5d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("Use Opacity for transparency"));
    }

    [Test]
    public void ValidateRejectsMissingMaterialBindingTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].MaterialBindingId = "material:missing";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("missing material"));
    }

    [Test]
    public void ValidateRejectsCameraMaterialBindingTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var camera = DccRenderTestData.CreateCamera();
        scene.Cameras.Add(camera);
        var cameraNode = DccRenderTestData.CreateCameraNode();
        cameraNode.MaterialBindingId = "material:cube";
        scene.Nodes.Add(cameraNode);

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("only on mesh nodes"));
    }

    [Test]
    public void ValidateRejectsLightMaterialBindingTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateLight();
        scene.Lights.Add(light);
        var lightNode = DccRenderTestData.CreateLightNode();
        lightNode.MaterialBindingId = "material:cube";
        scene.Nodes.Add(lightNode);

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("only on mesh nodes"));
    }

    [Test]
    public void ValidateRejectsMeshMaterialBindingWhenPerTriangleMaterialsArePresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials.Add(DccRenderTestData.CreateSecondaryMaterial());
        DccRenderTestData.ApplyTwoTriangleQuadMesh(scene);
        scene.Nodes[0].MaterialBindingId = "material:cube";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("per-triangle material indices"));
    }

    [Test]
    public void ValidateRejectsMeshNodeWithCameraReferenceTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].CameraId = "camera:main";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("cannot reference CameraId or LightId"));
    }

    [Test]
    public void ValidateRejectsCameraNodeWithMeshReferenceTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        var cameraNode = DccRenderTestData.CreateCameraNode();
        cameraNode.MeshId = "mesh:cube";
        scene.Nodes.Add(cameraNode);

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("cannot reference MeshId or LightId"));
    }

    [Test]
    public void ValidateRejectsLightNodeWithCameraReferenceTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        var lightNode = DccRenderTestData.CreateLightNode();
        lightNode.CameraId = "camera:main";
        scene.Nodes.Add(lightNode);

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("cannot reference MeshId or CameraId"));
    }

    [Test]
    public void ValidateRejectsOutOfRangeMaterialAlphaClipThresholdTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaClipThreshold = 1.5d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("alpha clip threshold in the [0, 1] range"));
    }

    [Test]
    public void ValidateRejectsCustomAlphaClipThresholdOutsideClipModeTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Blend;
        scene.Materials[0].AlphaClipThreshold = 0.2d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("only when AlphaMode is Clip"));
    }

    [Test]
    public void ValidateRejectsClipAlphaModeWithoutOpacityControlTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("only when opacity control is present"));
    }

    [Test]
    public void ValidateRejectsHashedAlphaModeWithoutOpacityControlTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Hashed;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("only when opacity control is present"));
    }

    [Test]
    public void ValidateRejectsOutOfRangeMaterialOpacityTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].Opacity = 1.5d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("opacity in the [0, 1] range"));
    }

    [Test]
    public void ValidateRejectsOutOfRangeMaterialMetallicTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].Metallic = 1.5d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("metallic in the [0, 1] range"));
    }

    [Test]
    public void ValidateRejectsOutOfRangeMaterialRoughnessTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].Roughness = -0.1d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("roughness in the [0, 1] range"));
    }

    [Test]
    public void ValidateRejectsNegativeMaterialNormalStrengthTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].NormalStrength = -1d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("non-negative normal strength"));
    }

    [Test]
    public void ValidateRejectsCustomNormalStrengthWithoutNormalTextureTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].NormalStrength = 2d;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("normal texture slot is present"));
    }

    [Test]
    public void ValidateRejectsInvalidCameraClipPlanesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Cameras[^1].NearClip = 500d;
        scene.Cameras[^1].FarClip = 0.1d;
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("NearClip is less than FarClip"));
    }

    [Test]
    public void ValidateRejectsUnsupportedLightKindTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Lights[^1].Kind = (DccLightKind)999;
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("unsupported light kind"));
    }

    [Test]
    public void ValidateRejectsLightColorAlphaTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Lights[^1].Color.A = 0.5d;
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("light color alpha to remain 1"));
    }

    [Test]
    public void ValidateRejectsNonPositiveLightIntensityTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Lights[^1].Intensity = 0d;
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("positive intensity"));
    }

    [Test]
    public void ValidateRejectsNonPositivePointLightRangeTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Lights[^1].Range = 0d;
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("positive range for point and spot lights"));
    }

    [Test]
    public void ValidateRejectsCustomSunLightRangeTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSunLight());
        scene.Lights[^1].Range = 100d;
        scene.Nodes.Add(DccRenderTestData.CreateSunLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("custom range only for point and spot lights"));
    }

    [Test]
    public void ValidateRejectsOutOfRangeSpotLightAngleTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSpotLight());
        scene.Lights[^1].SpotAngleDegrees = 181d;
        scene.Nodes.Add(DccRenderTestData.CreateSpotLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("spot angle in the (0, 180] range"));
    }

    [Test]
    public void ValidateRejectsCustomSpotAngleForNonSpotLightTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Lights[^1].SpotAngleDegrees = 20d;
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneContractValidator.Validate(scene));

        Assert.That(exception!.Message, Does.Contain("custom spot angle only when Kind is Spot"));
    }

    #endregion
}
