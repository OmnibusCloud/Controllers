using System.Collections.Generic;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Controller.Render.Dcc.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Tests.Services;

[TestFixture]
public sealed class DccSceneBuildInputFactoryTests
{
    #region Tests

    [Test]
    public void CreateValidSceneBuildInputTest()
    {
        var scene = DccRenderTestData.CreateValidScene();

        var buildInput = DccSceneBuildInputFactory.Create(scene);

        Assert.Multiple(() =>
        {
            Assert.That(buildInput.Scene, Is.Not.SameAs(scene));
            Assert.That(buildInput.Scene.SceneName, Is.EqualTo("TestScene"));
            Assert.That(buildInput.UnitsToMetersScale, Is.EqualTo(0.01d));
            Assert.That(buildInput.NodesById.Count, Is.EqualTo(1));
            Assert.That(buildInput.MeshesById.Count, Is.EqualTo(1));
            Assert.That(buildInput.MaterialsById.Count, Is.EqualTo(1));
            Assert.That(buildInput.ImageAssetsById.Count, Is.EqualTo(1));
            Assert.That(buildInput.ImageAttachmentsByImageId.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void CreateResolvesImageAttachmentByRelativePathTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment());

        var buildInput = DccSceneBuildInputFactory.Create(scene);

        Assert.Multiple(() =>
        {
            Assert.That(buildInput.ImageAttachmentsByImageId.ContainsKey("image:albedo"), Is.True);
            Assert.That(buildInput.ImageAttachmentsByImageId["image:albedo"].RelativePath, Is.EqualTo("textures/albedo.png"));
        });
    }

    [Test]
    public void CreateRejectsOutOfRangeTriangleIndexTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Meshes[0].TriangleIndices[0] = 99;

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("triangle index"));
    }

    [Test]
    public void CreateRejectsMissingParentNodeTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].ParentId = "node:missing";

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("parent node"));
    }

    [Test]
    public void CreateRejectsMismatchedMaterialIndicesCountTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        DccRenderTestData.ApplyTwoTriangleQuadMesh(scene);
        scene.Meshes[0].MaterialIndices = [0];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("material indices count"));
    }

    [Test]
    public void CreateRejectsOutOfRangeMaterialIndexTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Meshes[0].MaterialIndices = [4];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range material index"));
    }

    [Test]
    public void CreateRejectsDuplicateTransformKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].TransformKeyframes =
        [
            DccRenderTestData.CreateTransformKeyframe(1, 1d, 2d, 3d),
            DccRenderTestData.CreateTransformKeyframe(1, 4d, 5d, 6d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate transform keyframe frame"));
    }

    [Test]
    public void CreateRejectsDuplicateCameraFovKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Cameras[^1].VerticalFovKeyframes =
        [
            DccRenderTestData.CreateCameraFovKeyframe(1, 45d),
            DccRenderTestData.CreateCameraFovKeyframe(1, 60d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate vertical-FOV keyframe frame"));
    }

    [Test]
    public void CreateRejectsCameraFovKeyframesForOrthographicCameraTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var camera = DccRenderTestData.CreateCamera();
        camera.IsPerspective = false;
        camera.VerticalFovKeyframes =
        [
            DccRenderTestData.CreateCameraFovKeyframe(1, 45d)
        ];
        scene.Cameras.Add(camera);
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("IsPerspective is true"));
    }

    [Test]
    public void CreateRejectsOutOfRangeCameraFovKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Cameras[^1].VerticalFovKeyframes =
        [
            DccRenderTestData.CreateCameraFovKeyframe(1, 180d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range vertical-FOV keyframe value"));
    }

    [Test]
    public void CreateRejectsDuplicateCameraNearClipKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Cameras[^1].NearClipKeyframes =
        [
            DccRenderTestData.CreateCameraNearClipKeyframe(1, 0.1d),
            DccRenderTestData.CreateCameraNearClipKeyframe(1, 0.5d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate near-clip keyframe frame"));
    }

    [Test]
    public void CreateRejectsDuplicateCameraFarClipKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Cameras[^1].FarClipKeyframes =
        [
            DccRenderTestData.CreateCameraFarClipKeyframe(1, 500d),
            DccRenderTestData.CreateCameraFarClipKeyframe(1, 600d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate far-clip keyframe frame"));
    }

    [Test]
    public void CreateRejectsOutOfRangeCameraNearClipKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Cameras[^1].NearClipKeyframes =
        [
            DccRenderTestData.CreateCameraNearClipKeyframe(1, 0d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range near-clip keyframe value"));
    }

    [Test]
    public void CreateRejectsInvalidCameraClipOrderingAtKeyedFrameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Cameras[^1].NearClipKeyframes =
        [
            DccRenderTestData.CreateCameraNearClipKeyframe(2, 700d)
        ];
        scene.Cameras[^1].FarClipKeyframes =
        [
            DccRenderTestData.CreateCameraFarClipKeyframe(2, 600d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("invalid ordering"));
    }

    [Test]
    public void CreateRejectsNonPositiveTransformKeyframeFrameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].TransformKeyframes =
        [
            DccRenderTestData.CreateTransformKeyframe(0, 1d, 2d, 3d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("non-positive transform keyframe frame"));
    }

    [Test]
    public void CreateRejectsDuplicateVisibilityKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].VisibilityKeyframes =
        [
            DccRenderTestData.CreateVisibilityKeyframe(1, true, true),
            DccRenderTestData.CreateVisibilityKeyframe(1, false, false)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate visibility keyframe frame"));
    }

    [Test]
    public void CreateRejectsNonPositiveVisibilityKeyframeFrameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].VisibilityKeyframes =
        [
            DccRenderTestData.CreateVisibilityKeyframe(0, true, true)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("non-positive visibility keyframe frame"));
    }

    [Test]
    public void CreateRejectsDuplicateMaterialOpacityKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].OpacityKeyframes =
        [
            DccRenderTestData.CreateMaterialOpacityKeyframe(1, 1d),
            DccRenderTestData.CreateMaterialOpacityKeyframe(1, 0.5d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate opacity keyframe frame"));
    }

    [Test]
    public void CreateRejectsDuplicateMaterialBaseColorKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].BaseColorKeyframes =
        [
            DccRenderTestData.CreateMaterialBaseColorKeyframe(1, 0.8d, 0.7d, 0.6d),
            DccRenderTestData.CreateMaterialBaseColorKeyframe(1, 0.2d, 0.3d, 1d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate base-color keyframe frame"));
    }

    [Test]
    public void CreateRejectsMaterialBaseColorKeyframeAlphaTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateMaterialBaseColorKeyframe(1, 0.8d, 0.7d, 0.6d);
        keyframe.Color.A = 0.5d;
        scene.Materials[0].BaseColorKeyframes = [keyframe];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("unsupported base-color keyframe alpha"));
    }

    [Test]
    public void CreateRejectsDuplicateTextureUvTransformKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots[0].UvTransformKeyframes =
        [
            DccRenderTestData.CreateTextureTransformKeyframe(1, 1d, 1d, 0d, 0d, 0d),
            DccRenderTestData.CreateTextureTransformKeyframe(1, 2d, 0.5d, 0.25d, -0.1d, 45d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate UV-transform keyframe frame"));
    }

    [Test]
    public void CreateRejectsNonPositiveTextureUvTransformKeyframeFrameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots[0].UvTransformKeyframes =
        [
            DccRenderTestData.CreateTextureTransformKeyframe(0, 2d, 0.5d, 0.25d, -0.1d, 45d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("non-positive UV-transform keyframe frame"));
    }

    [Test]
    public void CreateRejectsAlphaClipThresholdKeyframesForNonClipMaterialTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Blend;
        scene.Materials[0].AlphaClipThresholdKeyframes =
        [
            DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(1, 0.5d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("AlphaMode is Clip"));
    }

    [Test]
    public void CreateRejectsDuplicateMaterialAlphaClipThresholdKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;
        scene.Materials[0].Opacity = 0.5d;
        scene.Materials[0].AlphaClipThresholdKeyframes =
        [
            DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(1, 0.5d),
            DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(1, 0.2d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate alpha-clip-threshold keyframe frame"));
    }

    [Test]
    public void CreateRejectsOutOfRangeMaterialAlphaClipThresholdKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;
        scene.Materials[0].Opacity = 0.5d;
        scene.Materials[0].AlphaClipThresholdKeyframes =
        [
            DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(1, 1.5d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range alpha-clip-threshold keyframe value"));
    }

    [Test]
    public void CreateRejectsOutOfRangeMaterialOpacityKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].OpacityKeyframes =
        [
            DccRenderTestData.CreateMaterialOpacityKeyframe(1, 1.5d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range opacity keyframe value"));
    }

    [Test]
    public void CreateRejectsDuplicateMaterialMetallicKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].MetallicKeyframes =
        [
            DccRenderTestData.CreateMaterialMetallicKeyframe(1, 0.1d),
            DccRenderTestData.CreateMaterialMetallicKeyframe(1, 0.8d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate metallic keyframe frame"));
    }

    [Test]
    public void CreateRejectsOutOfRangeMaterialMetallicKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].MetallicKeyframes =
        [
            DccRenderTestData.CreateMaterialMetallicKeyframe(1, 1.5d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range metallic keyframe value"));
    }

    [Test]
    public void CreateRejectsDuplicateMaterialRoughnessKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].RoughnessKeyframes =
        [
            DccRenderTestData.CreateMaterialRoughnessKeyframe(1, 0.5d),
            DccRenderTestData.CreateMaterialRoughnessKeyframe(1, 0.2d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate roughness keyframe frame"));
    }

    [Test]
    public void CreateRejectsOutOfRangeMaterialRoughnessKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].RoughnessKeyframes =
        [
            DccRenderTestData.CreateMaterialRoughnessKeyframe(1, -0.1d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range roughness keyframe value"));
    }

    [Test]
    public void CreateRejectsNormalStrengthKeyframesWithoutNormalTextureTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].NormalStrengthKeyframes =
        [
            DccRenderTestData.CreateMaterialNormalStrengthKeyframe(1, 1d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("normal texture slot is present"));
    }

    [Test]
    public void CreateRejectsDuplicateMaterialNormalStrengthKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateNormalImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Normal,
            ImageAssetId = "image:normal"
        });
        scene.Materials[0].NormalStrengthKeyframes =
        [
            DccRenderTestData.CreateMaterialNormalStrengthKeyframe(1, 1d),
            DccRenderTestData.CreateMaterialNormalStrengthKeyframe(1, 2d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate normal-strength keyframe frame"));
    }

    [Test]
    public void CreateRejectsNegativeMaterialNormalStrengthKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateNormalImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Normal,
            ImageAssetId = "image:normal"
        });
        scene.Materials[0].NormalStrengthKeyframes =
        [
            DccRenderTestData.CreateMaterialNormalStrengthKeyframe(1, -1d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range normal-strength keyframe value"));
    }

    [Test]
    public void CreateRejectsDuplicateLightIntensityKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].IntensityKeyframes =
        [
            DccRenderTestData.CreateLightIntensityKeyframe(1, 1200d),
            DccRenderTestData.CreateLightIntensityKeyframe(1, 400d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate intensity keyframe frame"));
    }

    [Test]
    public void CreateRejectsNonPositiveLightIntensityKeyframeFrameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].IntensityKeyframes =
        [
            DccRenderTestData.CreateLightIntensityKeyframe(0, 1200d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("non-positive intensity keyframe frame"));
    }

    [Test]
    public void CreateRejectsDuplicateLightColorKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].ColorKeyframes =
        [
            DccRenderTestData.CreateLightColorKeyframe(1, 1d, 0.95d, 0.85d),
            DccRenderTestData.CreateLightColorKeyframe(1, 0.2d, 0.3d, 1d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate color keyframe frame"));
    }

    [Test]
    public void CreateRejectsNonPositiveLightColorKeyframeFrameTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].ColorKeyframes =
        [
            DccRenderTestData.CreateLightColorKeyframe(0, 1d, 0.95d, 0.85d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("non-positive color keyframe frame"));
    }

    [Test]
    public void CreateRejectsLightColorKeyframeAlphaTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var keyframe = DccRenderTestData.CreateLightColorKeyframe(1, 1d, 0.95d, 0.85d);
        keyframe.Color.A = 0.5d;
        scene.Lights[^1].ColorKeyframes = [keyframe];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("unsupported light-color keyframe alpha"));
    }

    [Test]
    public void CreateRejectsDuplicateSpotAngleKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSpotLight());
        scene.Nodes.Add(DccRenderTestData.CreateSpotLightNode());
        scene.Lights[^1].SpotAngleKeyframes =
        [
            DccRenderTestData.CreateSpotLightAngleKeyframe(1, 35d),
            DccRenderTestData.CreateSpotLightAngleKeyframe(1, 20d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate spot-angle keyframe frame"));
    }

    [Test]
    public void CreateRejectsSpotAngleKeyframesForNonSpotLightTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].SpotAngleKeyframes =
        [
            DccRenderTestData.CreateSpotLightAngleKeyframe(1, 20d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("Kind is Spot"));
    }

    [Test]
    public void CreateRejectsOutOfRangeSpotAngleKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSpotLight());
        scene.Nodes.Add(DccRenderTestData.CreateSpotLightNode());
        scene.Lights[^1].SpotAngleKeyframes =
        [
            DccRenderTestData.CreateSpotLightAngleKeyframe(1, 190d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range spot-angle keyframe value"));
    }

    [Test]
    public void CreateRejectsDuplicateRangeKeyframeFramesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].RangeKeyframes =
        [
            DccRenderTestData.CreateLightRangeKeyframe(1, 25d),
            DccRenderTestData.CreateLightRangeKeyframe(1, 10d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("duplicate range keyframe frame"));
    }

    [Test]
    public void CreateRejectsRangeKeyframesForUnsupportedLightKindTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSunLight());
        scene.Nodes.Add(DccRenderTestData.CreateSunLightNode());
        scene.Lights[^1].RangeKeyframes =
        [
            DccRenderTestData.CreateLightRangeKeyframe(1, 10d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("Kind is Point or Spot"));
    }

    [Test]
    public void CreateRejectsOutOfRangeRangeKeyframeValueTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Lights[^1].RangeKeyframes =
        [
            DccRenderTestData.CreateLightRangeKeyframe(1, 0d)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() => DccSceneBuildInputFactory.Create(scene));

        Assert.That(exception!.Message, Does.Contain("out-of-range range keyframe value"));
    }

    #endregion
}
