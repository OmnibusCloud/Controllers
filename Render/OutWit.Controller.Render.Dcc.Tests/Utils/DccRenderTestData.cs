using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Dcc.Tests.Utils;

internal static class DccRenderTestData
{
    public static DccSceneData CreateValidScene()
    {
        return new DccSceneData
        {
            SceneName = "TestScene",
            SourceApplication = new DccApplicationData
            {
                ApplicationFamily = "3dsMax",
                ApplicationVersion = "2026",
                ExporterVersion = "1.0.0"
            },
            Units = new DccUnitSettingsData
            {
                LinearUnit = "centimeter",
                UnitsPerMeter = 100d
            },
            AxisSystem = new DccAxisSystemData
            {
                Handedness = "right",
                UpAxis = "Z",
                ForwardAxis = "Y"
            },
            RenderSettings = new DccRenderSettingsData(),
            Nodes =
            [
                new DccNodeData
                {
                    Id = "node:cube",
                    Name = "Cube",
                    Kind = DccNodeKind.Mesh,
                    MeshId = "mesh:cube",
                    MaterialBindingId = "material:cube",
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data { X = 1d, Y = 2d, Z = 3d },
                        Rotation = new DccQuaternionData { W = 1d },
                        Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
                    },
                    Visible = true,
                    Renderable = true
                }
            ],
            Meshes =
            [
                new DccMeshData
                {
                    Id = "mesh:cube",
                    Name = "CubeMesh",
                    Positions =
                    [
                        new DccVector3Data { X = -1d, Y = -1d, Z = 0d },
                        new DccVector3Data { X = 1d, Y = -1d, Z = 0d },
                        new DccVector3Data { X = 1d, Y = 1d, Z = 0d }
                    ],
                    Normals =
                    [
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d }
                    ],
                    Uv0 =
                    [
                        new DccVector2Data { X = 0d, Y = 0d },
                        new DccVector2Data { X = 1d, Y = 0d },
                        new DccVector2Data { X = 1d, Y = 1d }
                    ],
                    TriangleIndices = [0, 1, 2],
                    MaterialIndices = [0]
                }
            ],
            Materials =
            [
                new DccMaterialData
                {
                    Id = "material:cube",
                    Name = "CubeMaterial",
                    Kind = DccMaterialKind.PrincipledSurface,
                    BaseColor = new DccColorData { R = 0.8d, G = 0.7d, B = 0.6d, A = 1d },
                    AlphaMode = DccMaterialAlphaMode.Blend,
                    Opacity = 1d,
                    TextureSlots =
                    [
                        new DccTextureSlotData
                        {
                            Slot = DccTextureSlotKind.BaseColor,
                            ImageAssetId = "image:albedo",
                            UvScaleX = 1d,
                            UvScaleY = 1d
                        }
                    ]
                }
            ],
            ImageAssets =
            [
                new DccImageAssetData
                {
                    Id = "image:albedo",
                    Name = "Albedo",
                    SourcePath = "C:/textures/albedo.png",
                    RelativePath = "textures/albedo.png",
                    AssetKind = "ImageAsset"
                }
            ]
        };
    }

    public static RenderSceneAttachmentRefData CreateImageAttachment()
    {
        return CreateImageAttachment(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    public static RenderSceneAttachmentRefData CreateImageAttachment(Guid blobId)
    {
        return CreateImageAttachment(blobId, "C:/textures/albedo.png", "textures/albedo.png");
    }

    public static RenderSceneAttachmentRefData CreateImageAttachment(Guid blobId, string originalPath, string relativePath)
    {
        return new RenderSceneAttachmentRefData
        {
            Kind = "ImageAsset",
            BlobId = blobId,
            OriginalPath = originalPath,
            RelativePath = relativePath,
            PackagingStrategy = "SceneAttachmentBlob"
        };
    }

    public static DccImageAssetData CreateMetallicImageAsset()
    {
        return new DccImageAssetData
        {
            Id = "image:metallic",
            Name = "Metallic",
            SourcePath = "C:/textures/metallic.png",
            RelativePath = "textures/metallic.png",
            AssetKind = "ImageAsset"
        };
    }

    public static DccMaterialData CreateSecondaryMaterial()
    {
        return new DccMaterialData
        {
            Id = "material:secondary",
            Name = "SecondaryMaterial",
            Kind = DccMaterialKind.PrincipledSurface,
            BaseColor = new DccColorData { R = 0.2d, G = 0.6d, B = 0.9d, A = 1d },
            Opacity = 1d,
            Metallic = 0d,
            Roughness = 0.7d,
            NormalStrength = 1d
        };
    }

    public static void ApplyTwoTriangleQuadMesh(DccSceneData scene)
    {
        var mesh = scene.Meshes[0];
        mesh.Positions =
        [
            new DccVector3Data { X = -1d, Y = -1d, Z = 0d },
            new DccVector3Data { X = 1d, Y = -1d, Z = 0d },
            new DccVector3Data { X = 1d, Y = 1d, Z = 0d },
            new DccVector3Data { X = -1d, Y = 1d, Z = 0d }
        ];
        mesh.Normals =
        [
            new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
            new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
            new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
            new DccVector3Data { X = 0d, Y = 0d, Z = 1d }
        ];
        mesh.Uv0 =
        [
            new DccVector2Data { X = 0d, Y = 0d },
            new DccVector2Data { X = 1d, Y = 0d },
            new DccVector2Data { X = 1d, Y = 1d },
            new DccVector2Data { X = 0d, Y = 1d }
        ];
        mesh.TriangleIndices = [0, 1, 2, 0, 2, 3];
        mesh.MaterialIndices = [0, 1];
        scene.Nodes[0].MaterialBindingId = null;
    }

    public static DccImageAssetData CreateOpacityImageAsset()
    {
        return new DccImageAssetData
        {
            Id = "image:opacity",
            Name = "Opacity",
            SourcePath = "C:/textures/opacity.png",
            RelativePath = "textures/opacity.png",
            AssetKind = "ImageAsset"
        };
    }

    public static DccImageAssetData CreateNormalImageAsset()
    {
        return new DccImageAssetData
        {
            Id = "image:normal",
            Name = "Normal",
            SourcePath = "C:/textures/normal.png",
            RelativePath = "textures/normal.png",
            AssetKind = "ImageAsset"
        };
    }

    public static DccImageAssetData CreateRoughnessImageAsset()
    {
        return new DccImageAssetData
        {
            Id = "image:roughness",
            Name = "Roughness",
            SourcePath = "C:/textures/roughness.png",
            RelativePath = "textures/roughness.png",
            AssetKind = "ImageAsset"
        };
    }

    public static DccCameraData CreateCamera()
    {
        return new DccCameraData
        {
            Id = "camera:main",
            Name = "MainCamera",
            VerticalFovDegrees = 45d,
            NearClip = 0.1d,
            FarClip = 500d,
            IsPerspective = true
        };
    }

    public static DccScalarKeyframeData CreateCameraFovKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateMaterialOpacityKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateMaterialAlphaClipThresholdKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccColorKeyframeData CreateMaterialBaseColorKeyframe(int frame, double r, double g, double b)
    {
        return new DccColorKeyframeData
        {
            Frame = frame,
            Color = new DccColorData { R = r, G = g, B = b, A = 1d },
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateMaterialMetallicKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateMaterialRoughnessKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateMaterialNormalStrengthKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccTextureTransformKeyframeData CreateTextureTransformKeyframe(int frame, double scaleX, double scaleY, double offsetX, double offsetY, double rotationDegrees)
    {
        return new DccTextureTransformKeyframeData
        {
            Frame = frame,
            UvScaleX = scaleX,
            UvScaleY = scaleY,
            UvOffsetX = offsetX,
            UvOffsetY = offsetY,
            UvRotationDegrees = rotationDegrees,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateCameraNearClipKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateCameraFarClipKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccNodeData CreateCameraNode()
    {
        return new DccNodeData
        {
            Id = "node:camera-main",
            Name = "CameraMain",
            Kind = DccNodeKind.Camera,
            CameraId = "camera:main",
            LocalTransform = new DccTransformData
            {
                Translation = new DccVector3Data { X = 5d, Y = -5d, Z = 3d },
                Rotation = new DccQuaternionData
                {
                    X = 0.5099921226501465d,
                    Y = 0.21124568581581116d,
                    Z = 0.3190954625606537d,
                    W = 0.7703644633293152d
                },
                Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
            },
            Visible = true,
            Renderable = true
        };
    }

    public static DccLightData CreateLight()
    {
        return new DccLightData
        {
            Id = "light:key",
            Name = "KeyLight",
            Kind = DccLightKind.Point,
            Color = new DccColorData { R = 1d, G = 0.95d, B = 0.85d, A = 1d },
            Intensity = 1200d,
            Range = 25d,
            SpotAngleDegrees = 45d
        };
    }

    public static DccScalarKeyframeData CreateLightIntensityKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccColorKeyframeData CreateLightColorKeyframe(int frame, double r, double g, double b)
    {
        return new DccColorKeyframeData
        {
            Frame = frame,
            Color = new DccColorData { R = r, G = g, B = b, A = 1d },
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateSpotLightAngleKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateLightRangeKeyframe(int frame, double value)
    {
        return new DccScalarKeyframeData
        {
            Frame = frame,
            Value = value,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccNodeData CreateLightNode()
    {
        return new DccNodeData
        {
            Id = "node:key-light",
            Name = "KeyLightNode",
            Kind = DccNodeKind.Light,
            LightId = "light:key",
            LocalTransform = new DccTransformData
            {
                Translation = new DccVector3Data { X = 4d, Y = -4d, Z = 6d },
                Rotation = new DccQuaternionData { W = 1d },
                Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
            },
            Visible = true,
            Renderable = true
        };
    }

    public static DccLightData CreateSunLight()
    {
        return new DccLightData
        {
            Id = "light:sun",
            Name = "SunLight",
            Kind = DccLightKind.Sun,
            Color = new DccColorData { R = 1d, G = 0.98d, B = 0.9d, A = 1d },
            Intensity = 3d,
            Range = 10d,
            SpotAngleDegrees = 45d
        };
    }

    public static DccNodeData CreateSunLightNode()
    {
        return new DccNodeData
        {
            Id = "node:sun-light",
            Name = "SunLightNode",
            Kind = DccNodeKind.Light,
            LightId = "light:sun",
            LocalTransform = new DccTransformData
            {
                Translation = new DccVector3Data { X = 0d, Y = 0d, Z = 10d },
                Rotation = new DccQuaternionData { W = 1d },
                Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
            },
            Visible = true,
            Renderable = true
        };
    }

    public static DccLightData CreateSpotLight()
    {
        return new DccLightData
        {
            Id = "light:spot",
            Name = "SpotLight",
            Kind = DccLightKind.Spot,
            Color = new DccColorData { R = 0.9d, G = 0.95d, B = 1d, A = 1d },
            Intensity = 800d,
            Range = 20d,
            SpotAngleDegrees = 35d
        };
    }

    public static DccNodeData CreateSpotLightNode()
    {
        return new DccNodeData
        {
            Id = "node:spot-light",
            Name = "SpotLightNode",
            Kind = DccNodeKind.Light,
            LightId = "light:spot",
            LocalTransform = new DccTransformData
            {
                Translation = new DccVector3Data { X = 3d, Y = -3d, Z = 5d },
                Rotation = new DccQuaternionData { W = 1d },
                Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
            },
            Visible = true,
            Renderable = true
        };
    }

    public static DccTransformKeyframeData CreateTransformKeyframe(int frame, double x, double y, double z)
    {
        return new DccTransformKeyframeData
        {
            Frame = frame,
            Transform = new DccTransformData
            {
                Translation = new DccVector3Data { X = x, Y = y, Z = z },
                Rotation = new DccQuaternionData { W = 1d },
                Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
            }
        };
    }

    public static DccVisibilityKeyframeData CreateVisibilityKeyframe(int frame, bool visible, bool renderable)
    {
        return new DccVisibilityKeyframeData
        {
            Frame = frame,
            Visible = visible,
            Renderable = renderable
        };
    }
}
