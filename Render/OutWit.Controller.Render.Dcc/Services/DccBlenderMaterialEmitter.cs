using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Models.Build;
using static OutWit.Controller.Render.Dcc.Services.DccBlenderPythonFormatter;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderMaterialEmitter
{
    #region Functions

    public static void AppendMaterialLines(List<string> lines, DccSceneBuildInput buildInput)
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

    private static bool HasUvTransform(DccTextureSlotData textureSlot)
    {
        return textureSlot.UvScaleX != 1d
               || textureSlot.UvScaleY != 1d
               || textureSlot.UvOffsetX != 0d
               || textureSlot.UvOffsetY != 0d
               || textureSlot.UvRotationDegrees != 0d;
    }

    #endregion
}
