using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Models.Build;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderSceneScriptGenerator
{
    #region Constants

    private const double MIN_CUSTOM_LIGHT_DISTANCE = 0.01d;

    #endregion

    #region Functions

    public static string Create(DccSceneBuildInput buildInput)
    {
        var lines = new List<string>
        {
            "import bpy",
            "import math",
            "import mathutils",
            "",
            "bpy.ops.wm.read_factory_settings(use_empty=True)",
            "scene = bpy.context.scene",
            $"scene.name = {ToPythonStringLiteral(buildInput.Scene.SceneName)}",
            "scene.unit_settings.system = 'METRIC'",
            $"scene.unit_settings.scale_length = {FormatDouble(buildInput.UnitsToMetersScale)}",
            $"scene.render.resolution_x = {buildInput.Scene.RenderSettings.ResolutionX}",
            $"scene.render.resolution_y = {buildInput.Scene.RenderSettings.ResolutionY}",
            $"scene.render.fps = {buildInput.Scene.RenderSettings.Fps}",
            $"scene.frame_start = {buildInput.Scene.RenderSettings.FrameStart}",
            $"scene.frame_end = {buildInput.Scene.RenderSettings.FrameEnd}",
            "objects_by_node_id = {}",
            "materials_by_id = {}",
            "images_by_id = {}",
            "",
            "def set_transform(obj, translation, rotation, scale):",
            "    obj.location = translation",
            "    obj.rotation_mode = 'QUATERNION'",
            "    obj.rotation_quaternion = rotation",
            "    obj.scale = scale",
            "",
            "CAMERA_LIGHT_LOCAL_AXIS_CORRECTION = mathutils.Quaternion((1.0, 0.0, 0.0), math.radians(-90.0))",
            "",
            "def set_transform_with_local_axis_correction(obj, translation, rotation, scale, axis_correction):",
            "    obj.location = translation",
            "    obj.rotation_mode = 'QUATERNION'",
            "    obj.rotation_quaternion = rotation @ axis_correction",
            "    obj.scale = scale",
            "",
            "def set_camera_vertical_fov(camera_data, fov_degrees):",
            "    camera_data.sensor_fit = 'VERTICAL'",
            "    camera_data.lens = camera_data.sensor_height / (2.0 * math.tan(math.radians(fov_degrees) / 2.0))",
            "",
            "def set_keyframe_interpolation(obj, data_path, frame, interpolation):",
            "    if obj.animation_data is None or obj.animation_data.action is None:",
            "        return",
            "    action = obj.animation_data.action",
            "    if not hasattr(action, 'fcurves'):",
            "        return",
            "    for fcurve in action.fcurves:",
            "        if fcurve.data_path != data_path:",
            "            continue",
            "        for keyframe in fcurve.keyframe_points:",
            "            if int(round(keyframe.co.x)) == frame:",
            "                keyframe.interpolation = interpolation",
            ""
        };

        AppendImageLines(lines, buildInput);
        AppendMaterialLines(lines, buildInput);
        AppendMeshNodeLines(lines, buildInput);
        AppendCameraNodeLines(lines, buildInput);
        AppendLightNodeLines(lines, buildInput);
        AppendParentingLines(lines, buildInput);
        AppendSceneCameraLine(lines, buildInput);

        return string.Join("\n", lines);
    }

    private static void AppendImageLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        foreach (var imageAsset in buildInput.Scene.ImageAssets)
        {
            var imageVariableName = $"image_{SanitizeIdentifier(imageAsset.Id)}";
            var imagePath = ResolveImagePath(buildInput, imageAsset);

            lines.Add($"{imageVariableName} = bpy.data.images.load({ToPythonStringLiteral(imagePath)}, check_existing=True)");
            lines.Add($"images_by_id[{ToPythonStringLiteral(imageAsset.Id)}] = {imageVariableName}");
        }

        if (buildInput.Scene.ImageAssets.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendMaterialLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        foreach (var material in buildInput.Scene.Materials)
        {
            var materialVariableName = $"material_{SanitizeIdentifier(material.Id)}";
            var baseColor = material.BaseColor;

            lines.Add($"{materialVariableName} = bpy.data.materials.new(name={ToPythonStringLiteral(material.Name)})");
            lines.Add($"{materialVariableName}.use_nodes = True");
            lines.Add($"{materialVariableName}_nodes = {materialVariableName}.node_tree.nodes");
            lines.Add($"{materialVariableName}_links = {materialVariableName}.node_tree.links");
            lines.Add($"{materialVariableName}_bsdf = {materialVariableName}_nodes['Principled BSDF']");
            lines.Add($"{materialVariableName}_bsdf.inputs['Alpha'].default_value = {FormatDouble(material.Opacity)}");

            var baseColorTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.BaseColor);
            var usesBaseColorControl = baseColorTexture != null || material.BaseColorKeyframes.Count > 0;
            if (usesBaseColorControl)
                AppendBaseColorControlLines(lines, materialVariableName, material, baseColorTexture != null);
            else
                lines.Add($"{materialVariableName}_bsdf.inputs['Base Color'].default_value = ({FormatDouble(baseColor.R)}, {FormatDouble(baseColor.G)}, {FormatDouble(baseColor.B)}, {FormatDouble(material.Opacity)})");

            if (baseColorTexture != null)
            {
                AppendTextureSlotLines(
                    lines,
                    materialVariableName,
                    baseColorTexture,
                    "base_color",
                    "Color",
                    usesBaseColorControl ? null : "Base Color",
                    setNonColorData: false);

                if (usesBaseColorControl)
                    AppendBaseColorTextureCombinationLines(lines, materialVariableName);
            }

            var metallicTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.Metallic);
            var usesMetallicControl = metallicTexture != null || material.Metallic != 0d || material.MetallicKeyframes.Count > 0;
            if (usesMetallicControl)
                AppendScalarBsdfControlLines(lines, materialVariableName, "metallic", material.Metallic, "Metallic", metallicTexture != null);
            else
                lines.Add($"{materialVariableName}_bsdf.inputs['Metallic'].default_value = {FormatDouble(material.Metallic)}");

            if (metallicTexture != null)
            {
                AppendTextureSlotLines(
                    lines,
                    materialVariableName,
                    metallicTexture,
                    "metallic",
                    "Color",
                    usesMetallicControl ? null : "Metallic",
                    setNonColorData: true);

                if (usesMetallicControl)
                    AppendScalarTextureCombinationLines(lines, materialVariableName, "metallic", "Color");
            }

            var roughnessTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.Roughness);
            var usesRoughnessControl = roughnessTexture != null || material.Roughness != 0.5d || material.RoughnessKeyframes.Count > 0;
            if (usesRoughnessControl)
                AppendScalarBsdfControlLines(lines, materialVariableName, "roughness", material.Roughness, "Roughness", roughnessTexture != null);
            else
                lines.Add($"{materialVariableName}_bsdf.inputs['Roughness'].default_value = {FormatDouble(material.Roughness)}");

            if (roughnessTexture != null)
            {
                AppendTextureSlotLines(
                    lines,
                    materialVariableName,
                    roughnessTexture,
                    "roughness",
                    "Color",
                    usesRoughnessControl ? null : "Roughness",
                    setNonColorData: true);

                if (usesRoughnessControl)
                    AppendScalarTextureCombinationLines(lines, materialVariableName, "roughness", "Color");
            }

            var opacityTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.Opacity);
            var usesOpacityControl = opacityTexture != null || material.Opacity != 1d || material.OpacityKeyframes.Count > 0;
            var requiresAlphaModeLines = material.AlphaMode != DccMaterialAlphaMode.Blend || usesOpacityControl;
            if (requiresAlphaModeLines)
                AppendAlphaModeLines(lines, materialVariableName, material);

            if (usesOpacityControl)
                AppendOpacityControlLines(lines, materialVariableName, material, opacityTexture != null);

            if (opacityTexture != null)
            {
                AppendTextureSlotLines(
                    lines,
                    materialVariableName,
                    opacityTexture,
                    "opacity",
                    "Alpha",
                    usesOpacityControl ? null : "Alpha",
                    setNonColorData: false);

                if (usesOpacityControl)
                    AppendOpacityTextureCombinationLines(lines, materialVariableName);
            }

            var normalTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.Normal);
            if (normalTexture != null)
                AppendNormalTextureSlotLines(lines, material, materialVariableName, normalTexture);

            if (usesBaseColorControl)
                AppendMaterialBaseColorAnimationLines(lines, materialVariableName, material);

            if (usesOpacityControl)
                AppendMaterialOpacityAnimationLines(lines, materialVariableName, material);

            if (material.AlphaMode == DccMaterialAlphaMode.Clip)
                AppendMaterialAlphaClipThresholdAnimationLines(lines, materialVariableName, material);

            if (usesMetallicControl)
                AppendMaterialScalarAnimationLines(lines, materialVariableName, "metallic", material.MetallicKeyframes);

            if (usesRoughnessControl)
                AppendMaterialScalarAnimationLines(lines, materialVariableName, "roughness", material.RoughnessKeyframes);

            if (normalTexture != null)
                AppendMaterialNormalStrengthAnimationLines(lines, materialVariableName, material);

            lines.Add($"materials_by_id[{ToPythonStringLiteral(material.Id)}] = {materialVariableName}");
            lines.Add(string.Empty);
        }
    }

    private static void AppendScalarBsdfControlLines(
        List<string> lines,
        string materialVariableName,
        string controlName,
        double value,
        string bsdfInputName,
        bool hasTexture)
    {
        var scalarValueVariableName = $"{controlName}_value_{materialVariableName}";
        var scalarSocketVariableName = $"{controlName}_socket_{materialVariableName}";

        lines.Add($"{scalarValueVariableName} = {materialVariableName}_nodes.new('ShaderNodeValue')");
        lines.Add($"{scalarValueVariableName}.label = {ToPythonStringLiteral(controlName)}");
        lines.Add($"{scalarValueVariableName}.outputs[0].default_value = {FormatDouble(value)}");
        lines.Add($"{scalarSocketVariableName} = {scalarValueVariableName}.outputs[0]");

        if (hasTexture)
        {
            var scalarMultiplyVariableName = $"{controlName}_multiply_{materialVariableName}";
            lines.Add($"{scalarMultiplyVariableName} = {materialVariableName}_nodes.new('ShaderNodeMath')");
            lines.Add($"{scalarMultiplyVariableName}.operation = 'MULTIPLY'");
            lines.Add($"{materialVariableName}_links.new({scalarSocketVariableName}, {scalarMultiplyVariableName}.inputs[1])");
            lines.Add($"{materialVariableName}_links.new({scalarMultiplyVariableName}.outputs['Value'], {materialVariableName}_bsdf.inputs['{bsdfInputName}'])");
        }
        else
        {
            lines.Add($"{materialVariableName}_links.new({scalarSocketVariableName}, {materialVariableName}_bsdf.inputs['{bsdfInputName}'])");
        }
    }

    private static void AppendScalarTextureCombinationLines(List<string> lines, string materialVariableName, string controlName, string outputName)
    {
        var textureVariableName = $"texture_{materialVariableName}_{controlName}";
        var scalarMultiplyVariableName = $"{controlName}_multiply_{materialVariableName}";
        lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['{outputName}'], {scalarMultiplyVariableName}.inputs[0])");
    }

    private static void AppendTextureSlotLines(
        List<string> lines,
        string materialVariableName,
        DccTextureSlotData textureSlot,
        string slotSuffix,
        string outputName,
        string? bsdfInputName,
        bool setNonColorData)
    {
        var textureVariableName = $"texture_{materialVariableName}_{slotSuffix}";
        lines.Add($"{textureVariableName} = {materialVariableName}_nodes.new('ShaderNodeTexImage')");
        lines.Add($"{textureVariableName}.image = images_by_id[{ToPythonStringLiteral(textureSlot.ImageAssetId)}]");
        AppendTextureVectorMappingLines(lines, materialVariableName, textureVariableName, slotSuffix, textureSlot);

        if (setNonColorData)
            lines.Add($"{textureVariableName}.image.colorspace_settings.name = 'Non-Color'");

        if (!string.IsNullOrWhiteSpace(bsdfInputName))
        {
            lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['{outputName}'], {materialVariableName}_bsdf.inputs['{bsdfInputName}'])");
        }
    }

    private static void AppendNormalTextureSlotLines(
        List<string> lines,
        DccMaterialData material,
        string materialVariableName,
        DccTextureSlotData textureSlot)
    {
        var textureVariableName = $"texture_{materialVariableName}_normal";
        var normalMapVariableName = $"normal_map_{materialVariableName}";
        var normalStrengthSocketVariableName = $"normal_strength_socket_{materialVariableName}";
        lines.Add($"{textureVariableName} = {materialVariableName}_nodes.new('ShaderNodeTexImage')");
        lines.Add($"{textureVariableName}.image = images_by_id[{ToPythonStringLiteral(textureSlot.ImageAssetId)}]");
        AppendTextureVectorMappingLines(lines, materialVariableName, textureVariableName, "normal", textureSlot);
        lines.Add($"{textureVariableName}.image.colorspace_settings.name = 'Non-Color'");
        lines.Add($"{normalMapVariableName} = {materialVariableName}_nodes.new('ShaderNodeNormalMap')");
        lines.Add($"{normalMapVariableName}.inputs['Strength'].default_value = {FormatDouble(material.NormalStrength)}");
        lines.Add($"{normalStrengthSocketVariableName} = {normalMapVariableName}.inputs['Strength']");
        lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['Color'], {normalMapVariableName}.inputs['Color'])");
        lines.Add($"{materialVariableName}_links.new({normalMapVariableName}.outputs['Normal'], {materialVariableName}_bsdf.inputs['Normal'])");
    }

    private static void AppendAlphaModeLines(List<string> lines, string materialVariableName, DccMaterialData material)
    {
        switch (material.AlphaMode)
        {
            case DccMaterialAlphaMode.Blend:
                lines.Add($"{materialVariableName}.blend_method = 'BLEND'");
                lines.Add($"if hasattr({materialVariableName}, 'shadow_method'):");
                lines.Add($"    {materialVariableName}.shadow_method = 'HASHED'");
                break;
            case DccMaterialAlphaMode.Clip:
                lines.Add($"{materialVariableName}.blend_method = 'CLIP'");
                lines.Add($"if hasattr({materialVariableName}, 'shadow_method'):");
                lines.Add($"    {materialVariableName}.shadow_method = 'CLIP'");
                lines.Add($"{materialVariableName}.alpha_threshold = {FormatDouble(material.AlphaClipThreshold)}");
                break;
            case DccMaterialAlphaMode.Hashed:
                lines.Add($"{materialVariableName}.blend_method = 'HASHED'");
                lines.Add($"if hasattr({materialVariableName}, 'shadow_method'):");
                lines.Add($"    {materialVariableName}.shadow_method = 'HASHED'");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(material.AlphaMode), material.AlphaMode, null);
        }
    }

    private static void AppendBaseColorControlLines(List<string> lines, string materialVariableName, DccMaterialData material, bool hasBaseColorTexture)
    {
        var baseColorValueVariableName = $"base_color_value_{materialVariableName}";
        var baseColorSocketVariableName = $"base_color_socket_{materialVariableName}";
        var baseColorPathVariableName = $"base_color_path_{materialVariableName}";

        lines.Add($"{baseColorValueVariableName} = {materialVariableName}_nodes.new('ShaderNodeRGB')");
        lines.Add($"{baseColorValueVariableName}.label = 'Base Color'");
        lines.Add($"{baseColorValueVariableName}.outputs[0].default_value = ({FormatDouble(material.BaseColor.R)}, {FormatDouble(material.BaseColor.G)}, {FormatDouble(material.BaseColor.B)}, 1.0)");
        lines.Add($"{baseColorSocketVariableName} = {baseColorValueVariableName}.outputs[0]");
        lines.Add($"{baseColorPathVariableName} = {baseColorSocketVariableName}.path_from_id('default_value')");

        if (hasBaseColorTexture)
        {
            var baseColorMultiplyVariableName = $"base_color_multiply_{materialVariableName}";
            lines.Add($"{baseColorMultiplyVariableName} = {materialVariableName}_nodes.new('ShaderNodeMixRGB')");
            lines.Add($"{baseColorMultiplyVariableName}.blend_type = 'MULTIPLY'");
            lines.Add($"{baseColorMultiplyVariableName}.inputs['Fac'].default_value = 1.0");
            lines.Add($"{materialVariableName}_links.new({baseColorSocketVariableName}, {baseColorMultiplyVariableName}.inputs['Color2'])");
            lines.Add($"{materialVariableName}_links.new({baseColorMultiplyVariableName}.outputs['Color'], {materialVariableName}_bsdf.inputs['Base Color'])");
        }
        else
        {
            lines.Add($"{materialVariableName}_links.new({baseColorSocketVariableName}, {materialVariableName}_bsdf.inputs['Base Color'])");
        }
    }

    private static void AppendBaseColorTextureCombinationLines(List<string> lines, string materialVariableName)
    {
        var textureVariableName = $"texture_{materialVariableName}_base_color";
        var baseColorMultiplyVariableName = $"base_color_multiply_{materialVariableName}";
        lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['Color'], {baseColorMultiplyVariableName}.inputs['Color1'])");
    }

    private static void AppendMaterialBaseColorAnimationLines(List<string> lines, string materialVariableName, DccMaterialData material)
    {
        var baseColorSocketVariableName = $"base_color_socket_{materialVariableName}";
        var baseColorPathVariableName = $"base_color_path_{materialVariableName}";

        foreach (var keyframe in material.BaseColorKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{baseColorSocketVariableName}.default_value = ({FormatDouble(keyframe.Color.R)}, {FormatDouble(keyframe.Color.G)}, {FormatDouble(keyframe.Color.B)}, 1.0)");
            lines.Add($"{baseColorSocketVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {baseColorPathVariableName}, {keyframe.Frame}, {interpolationMode})");
        }

        if (material.BaseColorKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendOpacityControlLines(List<string> lines, string materialVariableName, DccMaterialData material, bool hasOpacityTexture)
    {
        var opacityValueVariableName = $"opacity_value_{materialVariableName}";
        var opacitySocketVariableName = $"opacity_socket_{materialVariableName}";
        var opacityPathVariableName = $"opacity_path_{materialVariableName}";

        lines.Add($"{opacityValueVariableName} = {materialVariableName}_nodes.new('ShaderNodeValue')");
        lines.Add($"{opacityValueVariableName}.label = 'Opacity'");
        lines.Add($"{opacityValueVariableName}.outputs[0].default_value = {FormatDouble(material.Opacity)}");
        lines.Add($"{opacitySocketVariableName} = {opacityValueVariableName}.outputs[0]");
        lines.Add($"{opacityPathVariableName} = {opacitySocketVariableName}.path_from_id('default_value')");

        if (hasOpacityTexture)
        {
            var opacityMultiplyVariableName = $"opacity_multiply_{materialVariableName}";
            lines.Add($"{opacityMultiplyVariableName} = {materialVariableName}_nodes.new('ShaderNodeMath')");
            lines.Add($"{opacityMultiplyVariableName}.operation = 'MULTIPLY'");
            lines.Add($"{materialVariableName}_links.new({opacitySocketVariableName}, {opacityMultiplyVariableName}.inputs[1])");
            lines.Add($"{materialVariableName}_links.new({opacityMultiplyVariableName}.outputs['Value'], {materialVariableName}_bsdf.inputs['Alpha'])");
        }
        else
        {
            lines.Add($"{materialVariableName}_links.new({opacitySocketVariableName}, {materialVariableName}_bsdf.inputs['Alpha'])");
        }
    }

    private static void AppendOpacityTextureCombinationLines(List<string> lines, string materialVariableName)
    {
        var textureVariableName = $"texture_{materialVariableName}_opacity";
        var opacityMultiplyVariableName = $"opacity_multiply_{materialVariableName}";
        lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['Alpha'], {opacityMultiplyVariableName}.inputs[0])");
    }

    private static void AppendMaterialOpacityAnimationLines(List<string> lines, string materialVariableName, DccMaterialData material)
    {
        var opacitySocketVariableName = $"opacity_socket_{materialVariableName}";
        var opacityPathVariableName = $"opacity_path_{materialVariableName}";

        foreach (var keyframe in material.OpacityKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{opacitySocketVariableName}.default_value = {FormatDouble(keyframe.Value)}");
            lines.Add($"{opacitySocketVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {opacityPathVariableName}, {keyframe.Frame}, {interpolationMode})");
        }

        if (material.OpacityKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendMaterialAlphaClipThresholdAnimationLines(List<string> lines, string materialVariableName, DccMaterialData material)
    {
        foreach (var keyframe in material.AlphaClipThresholdKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{materialVariableName}.alpha_threshold = {FormatDouble(keyframe.Value)}");
            lines.Add($"{materialVariableName}.keyframe_insert(data_path='alpha_threshold', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}, 'alpha_threshold', {keyframe.Frame}, {interpolationMode})");
        }

        if (material.AlphaClipThresholdKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendMaterialScalarAnimationLines(
        List<string> lines,
        string materialVariableName,
        string controlName,
        IReadOnlyList<DccScalarKeyframeData> keyframes)
    {
        var scalarSocketVariableName = $"{controlName}_socket_{materialVariableName}";
        var scalarPathVariableName = $"{controlName}_path_{materialVariableName}";

        lines.Add($"{scalarPathVariableName} = {scalarSocketVariableName}.path_from_id('default_value')");

        foreach (var keyframe in keyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{scalarSocketVariableName}.default_value = {FormatDouble(keyframe.Value)}");
            lines.Add($"{scalarSocketVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {scalarPathVariableName}, {keyframe.Frame}, {interpolationMode})");
        }

        if (keyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendMaterialNormalStrengthAnimationLines(List<string> lines, string materialVariableName, DccMaterialData material)
    {
        var normalStrengthSocketVariableName = $"normal_strength_socket_{materialVariableName}";
        var normalStrengthPathVariableName = $"normal_strength_path_{materialVariableName}";

        lines.Add($"{normalStrengthPathVariableName} = {normalStrengthSocketVariableName}.path_from_id('default_value')");

        foreach (var keyframe in material.NormalStrengthKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{normalStrengthSocketVariableName}.default_value = {FormatDouble(keyframe.Value)}");
            lines.Add($"{normalStrengthSocketVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {normalStrengthPathVariableName}, {keyframe.Frame}, {interpolationMode})");
        }

        if (material.NormalStrengthKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendTextureVectorMappingLines(
        List<string> lines,
        string materialVariableName,
        string textureVariableName,
        string slotSuffix,
        DccTextureSlotData textureSlot)
    {
        if (!HasUvTransform(textureSlot) && textureSlot.UvTransformKeyframes.Count == 0)
            return;

        var texCoordVariableName = $"texcoord_{materialVariableName}_{slotSuffix}";
        var mappingVariableName = $"mapping_{materialVariableName}_{slotSuffix}";
        var mappingLocationVariableName = $"{mappingVariableName}_location";
        var mappingRotationVariableName = $"{mappingVariableName}_rotation";
        var mappingScaleVariableName = $"{mappingVariableName}_scale";
        lines.Add($"{texCoordVariableName} = {materialVariableName}_nodes.new('ShaderNodeTexCoord')");
        lines.Add($"{mappingVariableName} = {materialVariableName}_nodes.new('ShaderNodeMapping')");
        lines.Add($"{mappingVariableName}.vector_type = 'TEXTURE'");
        lines.Add($"{mappingVariableName}.inputs['Location'].default_value[0] = {FormatDouble(textureSlot.UvOffsetX)}");
        lines.Add($"{mappingVariableName}.inputs['Location'].default_value[1] = {FormatDouble(textureSlot.UvOffsetY)}");
        lines.Add($"{mappingVariableName}.inputs['Rotation'].default_value[2] = math.radians({FormatDouble(textureSlot.UvRotationDegrees)})");
        lines.Add($"{mappingVariableName}.inputs['Scale'].default_value[0] = {FormatDouble(textureSlot.UvScaleX)}");
        lines.Add($"{mappingVariableName}.inputs['Scale'].default_value[1] = {FormatDouble(textureSlot.UvScaleY)}");
        lines.Add($"{mappingLocationVariableName} = {mappingVariableName}.inputs['Location']");
        lines.Add($"{mappingRotationVariableName} = {mappingVariableName}.inputs['Rotation']");
        lines.Add($"{mappingScaleVariableName} = {mappingVariableName}.inputs['Scale']");
        lines.Add($"{materialVariableName}_links.new({texCoordVariableName}.outputs['UV'], {mappingVariableName}.inputs['Vector'])");
        lines.Add($"{materialVariableName}_links.new({mappingVariableName}.outputs['Vector'], {textureVariableName}.inputs['Vector'])");
        AppendTextureVectorMappingAnimationLines(lines, materialVariableName, mappingVariableName, textureSlot);
    }

    private static void AppendTextureVectorMappingAnimationLines(
        List<string> lines,
        string materialVariableName,
        string mappingVariableName,
        DccTextureSlotData textureSlot)
    {
        if (textureSlot.UvTransformKeyframes.Count == 0)
            return;

        var mappingLocationVariableName = $"{mappingVariableName}_location";
        var mappingRotationVariableName = $"{mappingVariableName}_rotation";
        var mappingScaleVariableName = $"{mappingVariableName}_scale";
        var locationPathVariableName = $"{mappingVariableName}_location_path";
        var rotationPathVariableName = $"{mappingVariableName}_rotation_path";
        var scalePathVariableName = $"{mappingVariableName}_scale_path";

        lines.Add($"{locationPathVariableName} = {mappingLocationVariableName}.path_from_id('default_value')");
        lines.Add($"{rotationPathVariableName} = {mappingRotationVariableName}.path_from_id('default_value')");
        lines.Add($"{scalePathVariableName} = {mappingScaleVariableName}.path_from_id('default_value')");

        foreach (var keyframe in textureSlot.UvTransformKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{mappingLocationVariableName}.default_value[0] = {FormatDouble(keyframe.UvOffsetX)}");
            lines.Add($"{mappingLocationVariableName}.default_value[1] = {FormatDouble(keyframe.UvOffsetY)}");
            lines.Add($"{mappingRotationVariableName}.default_value[2] = math.radians({FormatDouble(keyframe.UvRotationDegrees)})");
            lines.Add($"{mappingScaleVariableName}.default_value[0] = {FormatDouble(keyframe.UvScaleX)}");
            lines.Add($"{mappingScaleVariableName}.default_value[1] = {FormatDouble(keyframe.UvScaleY)}");
            lines.Add($"{mappingLocationVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame}, index=0)");
            lines.Add($"{mappingLocationVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame}, index=1)");
            lines.Add($"{mappingRotationVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame}, index=2)");
            lines.Add($"{mappingScaleVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame}, index=0)");
            lines.Add($"{mappingScaleVariableName}.keyframe_insert(data_path='default_value', frame={keyframe.Frame}, index=1)");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {locationPathVariableName}, {keyframe.Frame}, {interpolationMode})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {rotationPathVariableName}, {keyframe.Frame}, {interpolationMode})");
            lines.Add($"set_keyframe_interpolation({materialVariableName}.node_tree, {scalePathVariableName}, {keyframe.Frame}, {interpolationMode})");
        }

        lines.Add(string.Empty);
    }

    private static void AppendLightNodeLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        foreach (var node in buildInput.Scene.Nodes.Where(me => me.Kind == DccNodeKind.Light))
        {
            var light = buildInput.Scene.Lights.First(me => me.Id == node.LightId);
            var lightVariableName = $"light_{SanitizeIdentifier(node.Id)}";
            var objectVariableName = $"object_{SanitizeIdentifier(node.Id)}";

            lines.Add($"{lightVariableName} = bpy.data.lights.new(name={ToPythonStringLiteral(light.Name)}, type={ToPythonStringLiteral(GetBlenderLightType(light.Kind))})");
            lines.Add($"{lightVariableName}.color = ({FormatDouble(light.Color.R)}, {FormatDouble(light.Color.G)}, {FormatDouble(light.Color.B)})");
            AppendLightColorAnimationLines(lines, lightVariableName, light);
            lines.Add($"{lightVariableName}.energy = {FormatDouble(light.Intensity)}");
            AppendLightAnimationLines(lines, lightVariableName, light);

            if (light.Kind is DccLightKind.Point or DccLightKind.Spot && light.Range > MIN_CUSTOM_LIGHT_DISTANCE)
            {
                lines.Add($"{lightVariableName}.use_custom_distance = True");
                lines.Add($"{lightVariableName}.cutoff_distance = {FormatDouble(light.Range)}");
                AppendLightRangeAnimationLines(lines, lightVariableName, light);
            }

            if (light.Kind == DccLightKind.Spot)
                lines.Add($"{lightVariableName}.spot_size = math.radians({FormatDouble(light.SpotAngleDegrees)})");

            AppendLightSpotAngleAnimationLines(lines, lightVariableName, light);

            lines.Add($"{objectVariableName} = bpy.data.objects.new({ToPythonStringLiteral(node.Name)}, {lightVariableName})");
            lines.Add($"scene.collection.objects.link({objectVariableName})");
            lines.Add($"set_transform_with_local_axis_correction({objectVariableName}, {BuildTranslationTuple(node.LocalTransform)}, mathutils.Quaternion({BuildQuaternionTuple(node.LocalTransform)}), {BuildScaleTuple(node.LocalTransform)}, CAMERA_LIGHT_LOCAL_AXIS_CORRECTION)");
            lines.Add($"{objectVariableName}.hide_render = {ToPythonBool(!node.Renderable)}");
            lines.Add($"{objectVariableName}.hide_viewport = {ToPythonBool(!node.Visible)}");
            AppendNodeAnimationLines(lines, objectVariableName, node);
            AppendNodeVisibilityAnimationLines(lines, objectVariableName, node);
            lines.Add($"objects_by_node_id[{ToPythonStringLiteral(node.Id)}] = {objectVariableName}");
            lines.Add(string.Empty);
        }
    }

    private static void AppendLightAnimationLines(List<string> lines, string lightVariableName, DccLightData light)
    {
        foreach (var keyframe in light.IntensityKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{lightVariableName}.energy = {FormatDouble(keyframe.Value)}");
            lines.Add($"{lightVariableName}.keyframe_insert(data_path='energy', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({lightVariableName}, 'energy', {keyframe.Frame}, {interpolationMode})");
        }

        if (light.IntensityKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendLightColorAnimationLines(List<string> lines, string lightVariableName, DccLightData light)
    {
        foreach (var keyframe in light.ColorKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{lightVariableName}.color = ({FormatDouble(keyframe.Color.R)}, {FormatDouble(keyframe.Color.G)}, {FormatDouble(keyframe.Color.B)})");
            lines.Add($"{lightVariableName}.keyframe_insert(data_path='color', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({lightVariableName}, 'color', {keyframe.Frame}, {interpolationMode})");
        }

        if (light.ColorKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendLightRangeAnimationLines(List<string> lines, string lightVariableName, DccLightData light)
    {
        foreach (var keyframe in light.RangeKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{lightVariableName}.cutoff_distance = {FormatDouble(keyframe.Value)}");
            lines.Add($"{lightVariableName}.keyframe_insert(data_path='cutoff_distance', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({lightVariableName}, 'cutoff_distance', {keyframe.Frame}, {interpolationMode})");
        }

        if (light.RangeKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendLightSpotAngleAnimationLines(List<string> lines, string lightVariableName, DccLightData light)
    {
        foreach (var keyframe in light.SpotAngleKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{lightVariableName}.spot_size = math.radians({FormatDouble(keyframe.Value)})");
            lines.Add($"{lightVariableName}.keyframe_insert(data_path='spot_size', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({lightVariableName}, 'spot_size', {keyframe.Frame}, {interpolationMode})");
        }

        if (light.SpotAngleKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendMeshNodeLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        foreach (var node in buildInput.Scene.Nodes.Where(me => me.Kind == DccNodeKind.Mesh))
        {
            var mesh = buildInput.MeshesById[node.MeshId!];
            var meshVariableName = $"mesh_{SanitizeIdentifier(node.Id)}";
            var objectVariableName = $"object_{SanitizeIdentifier(node.Id)}";
            var faces = Enumerable.Range(0, mesh.TriangleIndices.Count / 3)
                .Select(me => $"({mesh.TriangleIndices[me * 3]}, {mesh.TriangleIndices[me * 3 + 1]}, {mesh.TriangleIndices[me * 3 + 2]})");

            lines.Add($"{meshVariableName} = bpy.data.meshes.new({ToPythonStringLiteral(mesh.Name)})");
            lines.Add($"{meshVariableName}.from_pydata({BuildVector3List(mesh.Positions)}, [], [{string.Join(", ", faces)}])");
            lines.Add($"{objectVariableName} = bpy.data.objects.new({ToPythonStringLiteral(node.Name)}, {meshVariableName})");
            lines.Add($"scene.collection.objects.link({objectVariableName})");
            lines.Add($"set_transform({objectVariableName}, {BuildTranslationTuple(node.LocalTransform)}, {BuildQuaternionTuple(node.LocalTransform)}, {BuildScaleTuple(node.LocalTransform)})");
            lines.Add($"{objectVariableName}.hide_render = {ToPythonBool(!node.Renderable)}");
            lines.Add($"{objectVariableName}.hide_viewport = {ToPythonBool(!node.Visible)}");

            AppendMeshMaterialLines(lines, buildInput, node, mesh, meshVariableName);

            if (mesh.Uv0.Count > 0)
            {
                lines.Add($"{meshVariableName}_uv_layer = {meshVariableName}.uv_layers.new(name='UVMap')");
                lines.Add($"for polygon in {meshVariableName}.polygons:");
                lines.Add($"    for loop_index in polygon.loop_indices:");
                lines.Add($"        vertex_index = {meshVariableName}.loops[loop_index].vertex_index");
                lines.Add($"        uv = {BuildVector2List(mesh.Uv0)}[vertex_index]");
                lines.Add($"        {meshVariableName}_uv_layer.data[loop_index].uv = uv");
            }

            AppendNodeAnimationLines(lines, objectVariableName, node);
            AppendNodeVisibilityAnimationLines(lines, objectVariableName, node);
            lines.Add($"objects_by_node_id[{ToPythonStringLiteral(node.Id)}] = {objectVariableName}");
            lines.Add(string.Empty);
        }
    }

    private static void AppendMeshMaterialLines(
        List<string> lines,
        DccSceneBuildInput buildInput,
        DccNodeData node,
        DccMeshData mesh,
        string meshVariableName)
    {
        if (mesh.MaterialIndices.Count > 0)
        {
            var referencedMaterialIndices = mesh.MaterialIndices.Distinct().OrderBy(me => me).ToList();
            var localMaterialIndexBySceneIndex = new Dictionary<int, int>();

            for (var index = 0; index < referencedMaterialIndices.Count; index++)
            {
                var sceneMaterialIndex = referencedMaterialIndices[index];
                var materialId = buildInput.Scene.Materials[sceneMaterialIndex].Id;
                localMaterialIndexBySceneIndex[sceneMaterialIndex] = index;
                lines.Add($"{meshVariableName}.materials.append(materials_by_id[{ToPythonStringLiteral(materialId)}])");
            }

            for (var triangleIndex = 0; triangleIndex < mesh.MaterialIndices.Count; triangleIndex++)
            {
                var sceneMaterialIndex = mesh.MaterialIndices[triangleIndex];
                var localMaterialIndex = localMaterialIndexBySceneIndex[sceneMaterialIndex];
                lines.Add($"{meshVariableName}.polygons[{triangleIndex}].material_index = {localMaterialIndex}");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(node.MaterialBindingId))
            lines.Add($"{meshVariableName}.materials.append(materials_by_id[{ToPythonStringLiteral(node.MaterialBindingId)}])");
    }

    private static void AppendNodeAnimationLines(List<string> lines, string objectVariableName, DccNodeData node)
    {
        foreach (var keyframe in node.TransformKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"set_transform({objectVariableName}, {BuildTranslationTuple(keyframe.Transform)}, {BuildQuaternionTuple(keyframe.Transform)}, {BuildScaleTuple(keyframe.Transform)})");
            lines.Add($"{objectVariableName}.keyframe_insert(data_path='location', frame={keyframe.Frame})");
            lines.Add($"{objectVariableName}.keyframe_insert(data_path='rotation_quaternion', frame={keyframe.Frame})");
            lines.Add($"{objectVariableName}.keyframe_insert(data_path='scale', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({objectVariableName}, 'location', {keyframe.Frame}, {interpolationMode})");
            lines.Add($"set_keyframe_interpolation({objectVariableName}, 'rotation_quaternion', {keyframe.Frame}, {interpolationMode})");
            lines.Add($"set_keyframe_interpolation({objectVariableName}, 'scale', {keyframe.Frame}, {interpolationMode})");
        }

        if (node.TransformKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendNodeVisibilityAnimationLines(List<string> lines, string objectVariableName, DccNodeData node)
    {
        foreach (var keyframe in node.VisibilityKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{objectVariableName}.hide_viewport = {ToPythonBool(!keyframe.Visible)}");
            lines.Add($"{objectVariableName}.hide_render = {ToPythonBool(!keyframe.Renderable)}");
            lines.Add($"{objectVariableName}.keyframe_insert(data_path='hide_viewport', frame={keyframe.Frame})");
            lines.Add($"{objectVariableName}.keyframe_insert(data_path='hide_render', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({objectVariableName}, 'hide_viewport', {keyframe.Frame}, {interpolationMode})");
            lines.Add($"set_keyframe_interpolation({objectVariableName}, 'hide_render', {keyframe.Frame}, {interpolationMode})");
        }

        if (node.VisibilityKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendCameraFovAnimationLines(List<string> lines, string cameraVariableName, DccCameraData camera)
    {
        foreach (var keyframe in camera.VerticalFovKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"set_camera_vertical_fov({cameraVariableName}, {FormatDouble(keyframe.Value)})");
            lines.Add($"{cameraVariableName}.keyframe_insert(data_path='lens', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({cameraVariableName}, 'lens', {keyframe.Frame}, {interpolationMode})");
        }

        if (camera.VerticalFovKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendCameraClipAnimationLines(List<string> lines, string cameraVariableName, DccCameraData camera)
    {
        foreach (var keyframe in camera.NearClipKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{cameraVariableName}.clip_start = {FormatDouble(keyframe.Value)}");
            lines.Add($"{cameraVariableName}.keyframe_insert(data_path='clip_start', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({cameraVariableName}, 'clip_start', {keyframe.Frame}, {interpolationMode})");
        }

        foreach (var keyframe in camera.FarClipKeyframes.OrderBy(me => me.Frame))
        {
            var interpolationMode = ToPythonStringLiteral(GetBlenderInterpolationMode(keyframe.InterpolationMode));
            lines.Add($"scene.frame_set({keyframe.Frame})");
            lines.Add($"{cameraVariableName}.clip_end = {FormatDouble(keyframe.Value)}");
            lines.Add($"{cameraVariableName}.keyframe_insert(data_path='clip_end', frame={keyframe.Frame})");
            lines.Add($"set_keyframe_interpolation({cameraVariableName}, 'clip_end', {keyframe.Frame}, {interpolationMode})");
        }

        if (camera.NearClipKeyframes.Count > 0 || camera.FarClipKeyframes.Count > 0)
            lines.Add(string.Empty);
    }

    private static void AppendCameraNodeLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        foreach (var node in buildInput.Scene.Nodes.Where(me => me.Kind == DccNodeKind.Camera))
        {
            var camera = buildInput.Scene.Cameras.First(me => me.Id == node.CameraId);
            var cameraVariableName = $"camera_{SanitizeIdentifier(node.Id)}";
            var objectVariableName = $"object_{SanitizeIdentifier(node.Id)}";

            lines.Add($"{cameraVariableName} = bpy.data.cameras.new({ToPythonStringLiteral(camera.Name)})");
            lines.Add($"{cameraVariableName}.clip_start = {FormatDouble(camera.NearClip)}");
            lines.Add($"{cameraVariableName}.clip_end = {FormatDouble(camera.FarClip)}");
            AppendCameraClipAnimationLines(lines, cameraVariableName, camera);
            lines.Add($"{cameraVariableName}.type = {ToPythonStringLiteral(camera.IsPerspective ? "PERSP" : "ORTHO")}");
            if (camera.IsPerspective)
            {
                lines.Add($"set_camera_vertical_fov({cameraVariableName}, {FormatDouble(camera.VerticalFovDegrees)})");
                AppendCameraFovAnimationLines(lines, cameraVariableName, camera);
            }

            lines.Add($"{objectVariableName} = bpy.data.objects.new({ToPythonStringLiteral(node.Name)}, {cameraVariableName})");
            lines.Add($"scene.collection.objects.link({objectVariableName})");
            lines.Add($"set_transform_with_local_axis_correction({objectVariableName}, {BuildTranslationTuple(node.LocalTransform)}, mathutils.Quaternion({BuildQuaternionTuple(node.LocalTransform)}), {BuildScaleTuple(node.LocalTransform)}, CAMERA_LIGHT_LOCAL_AXIS_CORRECTION)");
            lines.Add($"{objectVariableName}.hide_render = {ToPythonBool(!node.Renderable)}");
            lines.Add($"{objectVariableName}.hide_viewport = {ToPythonBool(!node.Visible)}");
            AppendNodeAnimationLines(lines, objectVariableName, node);
            AppendNodeVisibilityAnimationLines(lines, objectVariableName, node);
            lines.Add($"objects_by_node_id[{ToPythonStringLiteral(node.Id)}] = {objectVariableName}");
            lines.Add(string.Empty);
        }
    }

    private static void AppendParentingLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        foreach (var node in buildInput.Scene.Nodes.Where(me => !string.IsNullOrWhiteSpace(me.ParentId)))
        {
            lines.Add($"objects_by_node_id[{ToPythonStringLiteral(node.Id)}].parent = objects_by_node_id[{ToPythonStringLiteral(node.ParentId!)}]");
        }

        if (buildInput.Scene.Nodes.Any(me => !string.IsNullOrWhiteSpace(me.ParentId)))
            lines.Add(string.Empty);
    }

    private static void AppendSceneCameraLine(List<string> lines, DccSceneBuildInput buildInput)
    {
        var firstCameraNode = buildInput.Scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Camera);
        if (firstCameraNode == null)
            return;

        lines.Add($"scene.camera = objects_by_node_id[{ToPythonStringLiteral(firstCameraNode.Id)}]");
    }

    private static string BuildQuaternionTuple(DccTransformData transform)
    {
        return $"({FormatDouble(transform.Rotation.W)}, {FormatDouble(transform.Rotation.X)}, {FormatDouble(transform.Rotation.Y)}, {FormatDouble(transform.Rotation.Z)})";
    }

    private static string BuildScaleTuple(DccTransformData transform)
    {
        return $"({FormatDouble(transform.Scale.X)}, {FormatDouble(transform.Scale.Y)}, {FormatDouble(transform.Scale.Z)})";
    }

    private static string BuildTranslationTuple(DccTransformData transform)
    {
        return $"({FormatDouble(transform.Translation.X)}, {FormatDouble(transform.Translation.Y)}, {FormatDouble(transform.Translation.Z)})";
    }

    private static string BuildVector2List(IReadOnlyList<DccVector2Data> values)
    {
        return $"[{string.Join(", ", values.Select(me => $"({FormatDouble(me.X)}, {FormatDouble(me.Y)})"))}]";
    }

    private static string BuildVector3List(IReadOnlyList<DccVector3Data> values)
    {
        return $"[{string.Join(", ", values.Select(me => $"({FormatDouble(me.X)}, {FormatDouble(me.Y)}, {FormatDouble(me.Z)})"))}]";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.0###############", CultureInfo.InvariantCulture);
    }

    private static bool HasUvTransform(DccTextureSlotData textureSlot)
    {
        return textureSlot.UvScaleX != 1d
               || textureSlot.UvScaleY != 1d
               || textureSlot.UvOffsetX != 0d
               || textureSlot.UvOffsetY != 0d
               || textureSlot.UvRotationDegrees != 0d;
    }

    private static string GetBlenderInterpolationMode(DccKeyframeInterpolationMode interpolationMode)
    {
        return interpolationMode switch
        {
            DccKeyframeInterpolationMode.Bezier => "BEZIER",
            DccKeyframeInterpolationMode.Linear => "LINEAR",
            DccKeyframeInterpolationMode.Constant => "CONSTANT",
            _ => throw new ArgumentOutOfRangeException(nameof(interpolationMode), interpolationMode, null)
        };
    }

    private static string GetBlenderLightType(DccLightKind lightKind)
    {
        return lightKind switch
        {
            DccLightKind.Point => "POINT",
            DccLightKind.Sun => "SUN",
            DccLightKind.Spot => "SPOT",
            _ => throw new ArgumentOutOfRangeException(nameof(lightKind), lightKind, null)
        };
    }

    private static string ResolveImagePath(DccSceneBuildInput buildInput, DccImageAssetData imageAsset)
    {
        if (buildInput.ImageAttachmentsByImageId.TryGetValue(imageAsset.Id, out var attachment))
            return attachment.RelativePath;

        if (!string.IsNullOrWhiteSpace(imageAsset.RelativePath))
            return imageAsset.RelativePath;

        return imageAsset.SourcePath;
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var symbol in value)
        {
            builder.Append(char.IsLetterOrDigit(symbol) ? char.ToLowerInvariant(symbol) : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static string ToPythonBool(bool value)
    {
        return value ? "True" : "False";
    }

    private static string ToPythonStringLiteral(string value)
    {
        return $"'{value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", "\\n")}'";
    }

    #endregion
}
