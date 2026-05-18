using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Utils;

internal static class DccModelTestData
{
    public static DccApplicationData CreateApplication()
    {
        return new DccApplicationData
        {
            ApplicationFamily = "3dsMax",
            ApplicationVersion = "2026",
            ExporterVersion = "1.0.0"
        };
    }

    public static DccUnitSettingsData CreateUnitSettings()
    {
        return new DccUnitSettingsData
        {
            LinearUnit = "centimeter",
            UnitsPerMeter = 100d
        };
    }

    public static DccAxisSystemData CreateAxisSystem()
    {
        return new DccAxisSystemData
        {
            Handedness = "right",
            UpAxis = "Z",
            ForwardAxis = "Y"
        };
    }

    public static DccRenderSettingsData CreateRenderSettings()
    {
        return new DccRenderSettingsData
        {
            ResolutionX = 1280,
            ResolutionY = 720,
            FrameStart = 1,
            FrameEnd = 1,
            Fps = 24,
            TargetEngine = RenderEngine.Cycles,
            Samples = 32
        };
    }

    public static DccVector2Data CreateVector2()
    {
        return new DccVector2Data
        {
            X = 0.25d,
            Y = 0.75d
        };
    }

    public static DccVector3Data CreateVector3()
    {
        return new DccVector3Data
        {
            X = 1d,
            Y = 2d,
            Z = 3d
        };
    }

    public static DccQuaternionData CreateQuaternion()
    {
        return new DccQuaternionData
        {
            X = 0d,
            Y = 0.5d,
            Z = 0d,
            W = 0.8660254037844386d
        };
    }

    public static DccColorData CreateColor()
    {
        return new DccColorData
        {
            R = 0.8d,
            G = 0.7d,
            B = 0.6d,
            A = 1d
        };
    }

    public static DccTransformData CreateTransform()
    {
        return new DccTransformData
        {
            Translation = CreateVector3(),
            Rotation = CreateQuaternion(),
            Scale = new DccVector3Data
            {
                X = 1d,
                Y = 1d,
                Z = 1d
            }
        };
    }

    public static DccTransformKeyframeData CreateTransformKeyframe()
    {
        return new DccTransformKeyframeData
        {
            Frame = 1,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier,
            Transform = CreateTransform()
        };
    }

    public static DccScalarKeyframeData CreateScalarKeyframe()
    {
        return new DccScalarKeyframeData
        {
            Frame = 1,
            Value = 4d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccColorKeyframeData CreateColorKeyframe()
    {
        return new DccColorKeyframeData
        {
            Frame = 1,
            Color = CreateColor(),
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateOpacityKeyframe()
    {
        return new DccScalarKeyframeData
        {
            Frame = 1,
            Value = 1d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateAlphaClipThresholdKeyframe()
    {
        return new DccScalarKeyframeData
        {
            Frame = 1,
            Value = 0.5d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateMetallicKeyframe()
    {
        return new DccScalarKeyframeData
        {
            Frame = 1,
            Value = 0.1d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateRoughnessKeyframe()
    {
        return new DccScalarKeyframeData
        {
            Frame = 1,
            Value = 0.5d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccScalarKeyframeData CreateNormalStrengthKeyframe()
    {
        return new DccScalarKeyframeData
        {
            Frame = 1,
            Value = 1d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccVisibilityKeyframeData CreateVisibilityKeyframe()
    {
        return new DccVisibilityKeyframeData
        {
            Frame = 1,
            Visible = true,
            Renderable = true,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccImageAssetData CreateImageAsset()
    {
        return new DccImageAssetData
        {
            Id = "image:albedo",
            Name = "Albedo",
            SourcePath = "C:/textures/albedo.png",
            RelativePath = "textures/albedo.png",
            AssetKind = "ImageAsset"
        };
    }

    public static DccTextureSlotData CreateTextureSlot()
    {
        return new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.BaseColor,
            ImageAssetId = "image:albedo",
            UvScaleX = 1d,
            UvScaleY = 1d,
            UvOffsetX = 0d,
            UvOffsetY = 0d,
            UvRotationDegrees = 0d,
            UvTransformKeyframes =
            [
                CreateTextureTransformKeyframe()
            ]
        };
    }

    public static DccTextureTransformKeyframeData CreateTextureTransformKeyframe()
    {
        return new DccTextureTransformKeyframeData
        {
            Frame = 1,
            UvScaleX = 1d,
            UvScaleY = 1d,
            UvOffsetX = 0d,
            UvOffsetY = 0d,
            UvRotationDegrees = 0d,
            InterpolationMode = DccKeyframeInterpolationMode.Bezier
        };
    }

    public static DccSceneData CreateScene()
    {
        return new DccSceneData
        {
            SceneName = "TestScene",
            SourceApplication = CreateApplication(),
            Units = CreateUnitSettings(),
            AxisSystem = CreateAxisSystem(),
            RenderSettings = CreateRenderSettings(),
            Nodes =
            [
                CreateNode(),
                new DccNodeData
                {
                    Id = "node:camera-main",
                    Name = "CameraMain",
                    Kind = DccNodeKind.Camera,
                    CameraId = "camera:main",
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data { X = 0d, Y = -5d, Z = 2d },
                        Rotation = new DccQuaternionData { X = 0d, Y = 0d, Z = 0d, W = 1d },
                        Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
                    }
                },
                new DccNodeData
                {
                    Id = "node:key-light",
                    Name = "KeyLight",
                    Kind = DccNodeKind.Light,
                    LightId = "light:key",
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data { X = 3d, Y = -3d, Z = 6d },
                        Rotation = new DccQuaternionData { X = 0d, Y = 0d, Z = 0d, W = 1d },
                        Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
                    }
                }
            ],
            Meshes =
            [
                CreateMesh()
            ],
            Cameras =
            [
                CreateCamera()
            ],
            Lights =
            [
                CreateLight()
            ],
            Materials =
            [
                CreateMaterial()
            ],
            ImageAssets =
            [
                CreateImageAsset()
            ],
            AttachedFiles =
            [
                new RenderSceneAttachmentRefData
                {
                    Kind = "ImageAsset",
                    BlobId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    OriginalPath = "C:/textures/albedo.png",
                    RelativePath = "textures/albedo.png",
                    PackagingStrategy = "BlobUpload"
                }
            ]
        };
    }

    public static DccMaterialData CreateMaterial()
    {
        return new DccMaterialData
        {
            Id = "material:cube",
            Name = "CubeMaterial",
            Kind = DccMaterialKind.PrincipledSurface,
            BaseColor = CreateColor(),
            BaseColorKeyframes =
            [
                CreateColorKeyframe()
            ],
            AlphaMode = DccMaterialAlphaMode.Blend,
            AlphaClipThreshold = 0.5d,
            AlphaClipThresholdKeyframes =
            [
                CreateAlphaClipThresholdKeyframe()
            ],
            Opacity = 1d,
            OpacityKeyframes =
            [
                CreateOpacityKeyframe()
            ],
            Metallic = 0.1d,
            MetallicKeyframes =
            [
                CreateMetallicKeyframe()
            ],
            Roughness = 0.5d,
            RoughnessKeyframes =
            [
                CreateRoughnessKeyframe()
            ],
            NormalStrength = 1d,
            NormalStrengthKeyframes =
            [
                CreateNormalStrengthKeyframe()
            ],
            TextureSlots =
            [
                CreateTextureSlot()
            ]
        };
    }

    public static DccNodeData CreateNode()
    {
        return new DccNodeData
        {
            Id = "node:cube",
            Name = "Cube",
            Kind = DccNodeKind.Mesh,
            MeshId = "mesh:cube",
            MaterialBindingId = "material:cube",
            LocalTransform = CreateTransform(),
            TransformKeyframes =
            [
                CreateTransformKeyframe()
            ],
            VisibilityKeyframes =
            [
                CreateVisibilityKeyframe()
            ],
            Visible = true,
            Renderable = true
        };
    }

    public static DccMeshData CreateMesh()
    {
        return new DccMeshData
        {
            Id = "mesh:cube",
            Name = "CubeMesh",
            Positions =
            [
                new DccVector3Data { X = -1d, Y = -1d, Z = 0d },
                new DccVector3Data { X = 1d, Y = -1d, Z = 0d },
                new DccVector3Data { X = 1d, Y = 1d, Z = 0d },
                new DccVector3Data { X = -1d, Y = 1d, Z = 0d }
            ],
            Normals =
            [
                new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                new DccVector3Data { X = 0d, Y = 0d, Z = 1d }
            ],
            Uv0 =
            [
                new DccVector2Data { X = 0d, Y = 0d },
                new DccVector2Data { X = 1d, Y = 0d },
                new DccVector2Data { X = 1d, Y = 1d },
                new DccVector2Data { X = 0d, Y = 1d }
            ],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
            MaterialIndices = [0]
        };
    }

    public static DccCameraData CreateCamera()
    {
        return new DccCameraData
        {
            Id = "camera:main",
            Name = "MainCamera",
            VerticalFovDegrees = 45d,
            VerticalFovKeyframes =
            [
                new DccScalarKeyframeData
                {
                    Frame = 1,
                    Value = 45d,
                    InterpolationMode = DccKeyframeInterpolationMode.Bezier
                }
            ],
            NearClip = 0.1d,
            NearClipKeyframes =
            [
                new DccScalarKeyframeData
                {
                    Frame = 1,
                    Value = 0.1d,
                    InterpolationMode = DccKeyframeInterpolationMode.Bezier
                }
            ],
            FarClip = 500d,
            FarClipKeyframes =
            [
                new DccScalarKeyframeData
                {
                    Frame = 1,
                    Value = 500d,
                    InterpolationMode = DccKeyframeInterpolationMode.Bezier
                }
            ],
            IsPerspective = true
        };
    }

    public static DccLightData CreateLight()
    {
        return new DccLightData
        {
            Id = "light:key",
            Name = "KeyLight",
            Kind = DccLightKind.Sun,
            Color = new DccColorData { R = 1d, G = 0.95d, B = 0.9d, A = 1d },
            ColorKeyframes =
            [
                CreateColorKeyframe()
            ],
            Intensity = 4d,
            IntensityKeyframes =
            [
                CreateScalarKeyframe()
            ],
            Range = 100d,
            SpotAngleDegrees = 45d
        };
    }

    public static DccLightData CreatePointLight()
    {
        return new DccLightData
        {
            Id = "light:point",
            Name = "PointLight",
            Kind = DccLightKind.Point,
            Color = new DccColorData { R = 1d, G = 0.95d, B = 0.9d, A = 1d },
            Intensity = 4d,
            IntensityKeyframes =
            [
                CreateScalarKeyframe()
            ],
            Range = 20d,
            RangeKeyframes =
            [
                new DccScalarKeyframeData
                {
                    Frame = 1,
                    Value = 20d,
                    InterpolationMode = DccKeyframeInterpolationMode.Bezier
                }
            ]
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
            SpotAngleDegrees = 35d,
            SpotAngleKeyframes =
            [
                new DccScalarKeyframeData
                {
                    Frame = 1,
                    Value = 35d,
                    InterpolationMode = DccKeyframeInterpolationMode.Bezier
                }
            ],
            Intensity = 800d,
            Range = 20d
        };
    }
}
