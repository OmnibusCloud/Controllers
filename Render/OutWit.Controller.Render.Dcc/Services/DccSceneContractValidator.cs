using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccSceneContractValidator
{
    #region Functions

    public static void Validate(DccSceneData scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (string.IsNullOrWhiteSpace(scene.SceneName))
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires a non-empty SceneName.");

        if (string.IsNullOrWhiteSpace(scene.SourceApplication.ApplicationFamily))
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires SourceApplication.ApplicationFamily.");

        if (string.IsNullOrWhiteSpace(scene.SourceApplication.ApplicationVersion)
            || string.IsNullOrWhiteSpace(scene.SourceApplication.ExporterVersion))
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires SourceApplication.ApplicationVersion and SourceApplication.ExporterVersion.");
        }

        if (string.IsNullOrWhiteSpace(scene.Units.LinearUnit) || scene.Units.UnitsPerMeter <= 0d)
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires valid unit settings.");

        if (scene.RenderSettings.ResolutionX <= 0
            || scene.RenderSettings.ResolutionY <= 0
            || scene.RenderSettings.FrameStart <= 0
            || scene.RenderSettings.FrameEnd < scene.RenderSettings.FrameStart
            || scene.RenderSettings.Fps <= 0
            || scene.RenderSettings.Samples <= 0)
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires valid render settings.");
        }

        if (!Enum.IsDefined(scene.RenderSettings.TargetEngine))
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires a supported render TargetEngine.");
        }

        if (string.IsNullOrWhiteSpace(scene.AxisSystem.Handedness)
            || string.IsNullOrWhiteSpace(scene.AxisSystem.UpAxis)
            || string.IsNullOrWhiteSpace(scene.AxisSystem.ForwardAxis))
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires a complete axis system.");
        }

        if (!new[] { "left", "right" }.Contains(scene.AxisSystem.Handedness, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires axis-system Handedness to be either 'left' or 'right'.");
        }

        if (!new[] { "X", "Y", "Z" }.Contains(scene.AxisSystem.UpAxis, StringComparer.OrdinalIgnoreCase)
            || !new[] { "X", "Y", "Z" }.Contains(scene.AxisSystem.ForwardAxis, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires axis-system UpAxis and ForwardAxis to use only X, Y, or Z.");
        }

        if (string.Equals(scene.AxisSystem.UpAxis, scene.AxisSystem.ForwardAxis, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Render.BuildBlendFromDccScene requires axis-system UpAxis and ForwardAxis to be different.");
        }

        ValidateUniqueIds(scene.Nodes.Select(me => me.Id), "node");
        ValidateUniqueIds(scene.Meshes.Select(me => me.Id), "mesh");
        ValidateUniqueIds(scene.Cameras.Select(me => me.Id), "camera");
        ValidateUniqueIds(scene.Lights.Select(me => me.Id), "light");
        ValidateUniqueIds(scene.Materials.Select(me => me.Id), "material");
        ValidateUniqueIds(scene.ImageAssets.Select(me => me.Id), "image");

        var meshIds = scene.Meshes.Select(me => me.Id).ToHashSet(StringComparer.Ordinal);
        var meshesById = scene.Meshes.ToDictionary(me => me.Id, StringComparer.Ordinal);
        var cameraIds = scene.Cameras.Select(me => me.Id).ToHashSet(StringComparer.Ordinal);
        var lightIds = scene.Lights.Select(me => me.Id).ToHashSet(StringComparer.Ordinal);
        var materialIds = scene.Materials.Select(me => me.Id).ToHashSet(StringComparer.Ordinal);
        var imageIds = scene.ImageAssets.Select(me => me.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var material in scene.Materials)
        {
            var seenTextureSlots = new HashSet<DccTextureSlotKind>();
            var hasNormalTextureSlot = material.TextureSlots.Any(me => me.Slot == DccTextureSlotKind.Normal);
            var hasOpacitySource = material.Opacity != 1d
                                   || material.OpacityKeyframes.Count > 0
                                   || material.TextureSlots.Any(me => me.Slot == DccTextureSlotKind.Opacity);

            if (material.Kind != DccMaterialKind.PrincipledSurface)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' uses unsupported material kind '{material.Kind}'.");
            }

            if (!Enum.IsDefined(material.AlphaMode))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' uses unsupported alpha mode '{material.AlphaMode}'.");
            }

            if (Math.Abs(material.BaseColor.A - 1d) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' requires base color alpha to remain 1. Use Opacity for transparency.");
            }

            if (material.AlphaClipThreshold < 0d || material.AlphaClipThreshold > 1d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' requires alpha clip threshold in the [0, 1] range.");
            }

            if (material.AlphaMode != DccMaterialAlphaMode.Clip && material.AlphaClipThreshold != 0.5d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' can use a custom alpha clip threshold only when AlphaMode is Clip.");
            }

            if (material.AlphaMode is DccMaterialAlphaMode.Clip or DccMaterialAlphaMode.Hashed && !hasOpacitySource)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' can use AlphaMode '{material.AlphaMode}' only when opacity control is present.");
            }

            if (material.Opacity < 0d || material.Opacity > 1d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' requires opacity in the [0, 1] range.");
            }

            if (material.Metallic < 0d || material.Metallic > 1d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' requires metallic in the [0, 1] range.");
            }

            if (material.Roughness < 0d || material.Roughness > 1d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' requires roughness in the [0, 1] range.");
            }

            if (material.NormalStrength < 0d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' requires non-negative normal strength.");
            }

            if (!hasNormalTextureSlot && material.NormalStrength != 1d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' can use custom normal strength only when a normal texture slot is present.");
            }

            foreach (var textureSlot in material.TextureSlots)
            {
                if (!Enum.IsDefined(textureSlot.Slot))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' references unsupported texture slot kind '{textureSlot.Slot}'.");
                }

                if (!seenTextureSlots.Add(textureSlot.Slot))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate texture slot kind '{textureSlot.Slot}'.");
                }

                if (!imageIds.Contains(textureSlot.ImageAssetId))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' references missing image asset '{textureSlot.ImageAssetId}'.");
                }
            }
        }

        foreach (var node in scene.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                throw new InvalidOperationException("Render.BuildBlendFromDccScene requires non-empty node ids.");

            switch (node.Kind)
            {
                case DccNodeKind.Mesh:
                    if (string.IsNullOrWhiteSpace(node.MeshId) || !meshIds.Contains(node.MeshId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene mesh node '{node.Id}' references missing mesh '{node.MeshId}'.");
                    }

                    if (!string.IsNullOrWhiteSpace(node.CameraId) || !string.IsNullOrWhiteSpace(node.LightId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene mesh node '{node.Id}' cannot reference CameraId or LightId. Mesh nodes may reference only MeshId and optional MaterialBindingId.");
                    }

                    if (!string.IsNullOrWhiteSpace(node.MaterialBindingId)
                        && meshesById[node.MeshId].MaterialIndices.Distinct().Skip(1).Any())
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene mesh node '{node.Id}' cannot use MaterialBindingId when mesh '{node.MeshId}' already contains per-triangle material indices.");
                    }
                    break;
                case DccNodeKind.Camera:
                    if (string.IsNullOrWhiteSpace(node.CameraId) || !cameraIds.Contains(node.CameraId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene camera node '{node.Id}' references missing camera '{node.CameraId}'.");
                    }

                    if (!string.IsNullOrWhiteSpace(node.MeshId) || !string.IsNullOrWhiteSpace(node.LightId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene camera node '{node.Id}' cannot reference MeshId or LightId. Camera nodes may reference only CameraId.");
                    }

                    if (!string.IsNullOrWhiteSpace(node.MaterialBindingId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene camera node '{node.Id}' cannot use MaterialBindingId. Material bindings are supported only on mesh nodes.");
                    }
                    break;
                case DccNodeKind.Light:
                    if (string.IsNullOrWhiteSpace(node.LightId) || !lightIds.Contains(node.LightId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene light node '{node.Id}' references missing light '{node.LightId}'.");
                    }

                    if (!string.IsNullOrWhiteSpace(node.MeshId) || !string.IsNullOrWhiteSpace(node.CameraId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene light node '{node.Id}' cannot reference MeshId or CameraId. Light nodes may reference only LightId.");
                    }

                    if (!string.IsNullOrWhiteSpace(node.MaterialBindingId))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene light node '{node.Id}' cannot use MaterialBindingId. Material bindings are supported only on mesh nodes.");
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Render.BuildBlendFromDccScene encountered unsupported node kind '{node.Kind}'.");
            }

            if (!string.IsNullOrWhiteSpace(node.MaterialBindingId) && !materialIds.Contains(node.MaterialBindingId))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene node '{node.Id}' references missing material '{node.MaterialBindingId}'.");
            }
        }

        foreach (var camera in scene.Cameras)
        {
            if (camera.IsPerspective && (camera.VerticalFovDegrees <= 0d || camera.VerticalFovDegrees > 180d))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene camera '{camera.Id}' requires perspective VerticalFovDegrees in the (0, 180] range.");
            }

            if (camera.NearClip <= 0d || camera.FarClip <= 0d || camera.NearClip >= camera.FarClip)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene camera '{camera.Id}' requires positive clipping planes where NearClip is less than FarClip.");
            }
        }

        foreach (var light in scene.Lights)
        {
            if (!Enum.IsDefined(light.Kind))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' uses unsupported light kind '{light.Kind}'.");
            }

            if (Math.Abs(light.Color.A - 1d) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' requires light color alpha to remain 1.");
            }

            if (light.Intensity <= 0d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' requires positive intensity.");
            }

            if (light.Kind is DccLightKind.Point or DccLightKind.Spot && light.Range <= 0d)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' requires positive range for point and spot lights.");
            }

            if (light.Kind == DccLightKind.Sun && Math.Abs(light.Range - 10d) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' can use a custom range only for point and spot lights.");
            }

            if (light.Kind != DccLightKind.Spot && Math.Abs(light.SpotAngleDegrees - 45d) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' can use a custom spot angle only when Kind is Spot.");
            }

            if (light.Kind == DccLightKind.Spot && (light.SpotAngleDegrees <= 0d || light.SpotAngleDegrees > 180d))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' requires spot angle in the (0, 180] range for spot lights.");
            }
        }
    }

    private static void ValidateUniqueIds(IEnumerable<string> ids, string family)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"Render.BuildBlendFromDccScene requires non-empty {family} ids.");

            if (!seen.Add(id))
                throw new InvalidOperationException($"Render.BuildBlendFromDccScene requires unique {family} ids. Duplicate: '{id}'.");
        }
    }

    #endregion
}
