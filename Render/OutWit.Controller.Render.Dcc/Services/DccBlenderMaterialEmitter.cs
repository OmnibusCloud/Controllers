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

            // Vertex-color driven channels: bind the mesh's per-corner 'Color' attribute (emitted
            // by the node emitter) through a Color Attribute node. A source-DCC vertex-color map
            // carries baked shading no flat color can reproduce (Lighting-Vertex's whole look).
            var usesVertexColors = material.BaseColorFromVertexColors || material.EmissionFromVertexColors;
            if (usesVertexColors)
            {
                lines.Add($"{materialVariableName}_vcol = {materialVariableName}_nodes.new('ShaderNodeVertexColor')");
                lines.Add($"{materialVariableName}_vcol.layer_name = 'Color'");
            }

            if (material.BaseColorFromVertexColors)
                lines.Add($"{materialVariableName}_links.new({materialVariableName}_vcol.outputs['Color'], {materialVariableName}_bsdf.inputs['Base Color'])");

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

            // Transmission / refraction (e.g. glass). Blender 4.0+/5.x Principled BSDF socket
            // names: 'Transmission Weight' and 'IOR'. Only emitted when the material is actually
            // transmissive so opaque materials keep the BSDF defaults.
            if (material.Transmission > 0d)
            {
                lines.Add($"{materialVariableName}_bsdf.inputs['Transmission Weight'].default_value = {FormatDouble(material.Transmission)}");
                lines.Add($"{materialVariableName}_bsdf.inputs['IOR'].default_value = {FormatDouble(material.Ior)}");
            }

            // Emission for self-illuminating materials. 3ds Max self-illumination makes the DIFFUSE
            // channel glow — with a base-color texture the texture itself must drive the emission
            // (a sky-dome sphere emits its cloud bitmap), not a flat colour.
            if (material.EmissionStrength > 0d)
            {
                var emission = material.EmissionColor;
                lines.Add($"{materialVariableName}_bsdf.inputs['Emission Color'].default_value = ({FormatDouble(emission.R)}, {FormatDouble(emission.G)}, {FormatDouble(emission.B)}, 1.0)");

                if (material.EmissionCameraOnly)
                {
                    // A no-GI source renderer (Scanline) shows a self-illuminated surface bright but
                    // never lights the scene with it — Lighting-Vertex's glowing boxes flooded the
                    // whole interior through Cycles GI. Restrict the emission to visibility rays.
                    lines.Add($"{materialVariableName}_lp = {materialVariableName}_nodes.new('ShaderNodeLightPath')");
                    lines.Add($"{materialVariableName}_emis_vis = {materialVariableName}_nodes.new('ShaderNodeMath')");
                    lines.Add($"{materialVariableName}_emis_vis.operation = 'MAXIMUM'");
                    lines.Add($"{materialVariableName}_emis_vis_2 = {materialVariableName}_nodes.new('ShaderNodeMath')");
                    lines.Add($"{materialVariableName}_emis_vis_2.operation = 'MAXIMUM'");
                    lines.Add($"{materialVariableName}_emis_scale = {materialVariableName}_nodes.new('ShaderNodeMath')");
                    lines.Add($"{materialVariableName}_emis_scale.operation = 'MULTIPLY'");
                    lines.Add($"{materialVariableName}_emis_scale.inputs[1].default_value = {FormatDouble(material.EmissionStrength)}");
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_lp.outputs['Is Camera Ray'], {materialVariableName}_emis_vis.inputs[0])");
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_lp.outputs['Is Glossy Ray'], {materialVariableName}_emis_vis.inputs[1])");
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_emis_vis.outputs['Value'], {materialVariableName}_emis_vis_2.inputs[0])");
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_lp.outputs['Is Transmission Ray'], {materialVariableName}_emis_vis_2.inputs[1])");
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_emis_vis_2.outputs['Value'], {materialVariableName}_emis_scale.inputs[0])");
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_emis_scale.outputs['Value'], {materialVariableName}_bsdf.inputs['Emission Strength'])");
                }
                else
                {
                    lines.Add($"{materialVariableName}_bsdf.inputs['Emission Strength'].default_value = {FormatDouble(material.EmissionStrength)}");
                }

                if (baseColorTexture != null)
                    lines.Add($"{materialVariableName}_links.new(texture_{materialVariableName}_base_color.outputs['Color'], {materialVariableName}_bsdf.inputs['Emission Color'])");

                // Vertex colors override both the flat emission color and a base-color texture:
                // when the source diffuse is vertex-colored the emission glows those colors too
                // (self-illum glows the diffuse), and an explicit self-illum vertex map wins.
                if (material.EmissionFromVertexColors || material.BaseColorFromVertexColors)
                    lines.Add($"{materialVariableName}_links.new({materialVariableName}_vcol.outputs['Color'], {materialVariableName}_bsdf.inputs['Emission Color'])");
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

            var bumpTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.Bump);
            if (bumpTexture != null && normalTexture == null)
                AppendBumpTextureSlotLines(lines, material, materialVariableName, bumpTexture);

            var displacementTexture = material.TextureSlots.FirstOrDefault(me => me.Slot == DccTextureSlotKind.Displacement);
            if (displacementTexture != null)
                AppendDisplacementTextureSlotLines(lines, material, materialVariableName, displacementTexture);

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

            if (material.BackfaceCull)
                AppendBackfaceCullLines(lines, materialVariableName);

            lines.Add($"materials_by_id[{ToPythonStringLiteral(material.Id)}] = {materialVariableName}");
            lines.Add(string.Empty);
        }
    }

    // Single-sided rendering: camera rays hitting a backface pass through (Transparent BSDF via a
    // Backfacing-driven mix), mirroring 3ds Max Scanline's backface culling — interiors authored
    // with inward-facing walls stay visible from a camera outside the room. EEVEE's native flag is
    // set too for viewport parity; Cycles uses the node chain.
    private static void AppendBackfaceCullLines(List<string> lines, string materialVariableName)
    {
        var geometryVariableName = $"backface_geometry_{materialVariableName}";
        var transparentVariableName = $"backface_transparent_{materialVariableName}";
        var mixVariableName = $"backface_mix_{materialVariableName}";
        var outputVariableName = $"backface_output_{materialVariableName}";

        lines.Add($"{geometryVariableName} = {materialVariableName}_nodes.new('ShaderNodeNewGeometry')");
        lines.Add($"{transparentVariableName} = {materialVariableName}_nodes.new('ShaderNodeBsdfTransparent')");
        lines.Add($"{mixVariableName} = {materialVariableName}_nodes.new('ShaderNodeMixShader')");
        lines.Add($"{outputVariableName} = {materialVariableName}_nodes['Material Output']");
        lines.Add($"{materialVariableName}_links.new({geometryVariableName}.outputs['Backfacing'], {mixVariableName}.inputs['Fac'])");
        lines.Add($"{materialVariableName}_links.new({materialVariableName}_bsdf.outputs['BSDF'], {mixVariableName}.inputs[1])");
        lines.Add($"{materialVariableName}_links.new({transparentVariableName}.outputs['BSDF'], {mixVariableName}.inputs[2])");
        lines.Add($"{materialVariableName}_links.new({mixVariableName}.outputs['Shader'], {outputVariableName}.inputs['Surface'])");
        lines.Add($"{materialVariableName}.use_backface_culling = True");
        lines.Add($"{materialVariableName}.blend_method = 'HASHED'");
        lines.Add($"if hasattr({materialVariableName}, 'shadow_method'):");
        lines.Add($"    {materialVariableName}.shadow_method = 'HASHED'");
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

    private static void AppendBumpTextureSlotLines(
        List<string> lines,
        DccMaterialData material,
        string materialVariableName,
        DccTextureSlotData textureSlot)
    {
        // A bump map is a grayscale HEIGHT map — it must go through a Bump node (Height input).
        // Feeding it into a normal-map node interprets heights as normal vectors and carves black
        // craters (hardwood's paint-swatch board with Smoke/Noise bumps).
        var textureVariableName = $"texture_{materialVariableName}_bump";
        var bumpNodeVariableName = $"bump_{materialVariableName}";
        lines.Add($"{textureVariableName} = {materialVariableName}_nodes.new('ShaderNodeTexImage')");
        lines.Add($"{textureVariableName}.image = images_by_id[{ToPythonStringLiteral(textureSlot.ImageAssetId)}]");
        AppendTextureVectorMappingLines(lines, materialVariableName, textureVariableName, "bump", textureSlot);
        lines.Add($"{textureVariableName}.image.colorspace_settings.name = 'Non-Color'");
        lines.Add($"{bumpNodeVariableName} = {materialVariableName}_nodes.new('ShaderNodeBump')");
        lines.Add($"{bumpNodeVariableName}.inputs['Strength'].default_value = {FormatDouble(material.NormalStrength)}");
        lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['Color'], {bumpNodeVariableName}.inputs['Height'])");
        lines.Add($"{materialVariableName}_links.new({bumpNodeVariableName}.outputs['Normal'], {materialVariableName}_bsdf.inputs['Normal'])");
    }

    private static void AppendDisplacementTextureSlotLines(
        List<string> lines,
        DccMaterialData material,
        string materialVariableName,
        DccTextureSlotData textureSlot)
    {
        // Displacement map -> Displacement node -> Material Output 'Displacement' input, with true
        // (geometry) displacement in Cycles. A Subdivision modifier is added to objects bound to a
        // displacement material (see DccBlenderNodeEmitter) so there is geometry to displace.
        var textureVariableName = $"texture_{materialVariableName}_displacement";
        var displacementNodeVariableName = $"displacement_{materialVariableName}";
        var outputNodeVariableName = $"output_{materialVariableName}";

        lines.Add($"{textureVariableName} = {materialVariableName}_nodes.new('ShaderNodeTexImage')");
        lines.Add($"{textureVariableName}.image = images_by_id[{ToPythonStringLiteral(textureSlot.ImageAssetId)}]");
        AppendTextureVectorMappingLines(lines, materialVariableName, textureVariableName, "displacement", textureSlot);
        lines.Add($"{textureVariableName}.image.colorspace_settings.name = 'Non-Color'");
        lines.Add($"{displacementNodeVariableName} = {materialVariableName}_nodes.new('ShaderNodeDisplacement')");
        lines.Add($"{displacementNodeVariableName}.inputs['Scale'].default_value = {FormatDouble(material.DisplacementScale)}");
        // Heights are measured FROM the surface (0 = no displacement), matching source-DCC
        // semantics. Blender's default midlevel 0.5 would push dark areas inward and halve the
        // effective peak height (MoonRock's spikes rendered soft and shallow).
        lines.Add($"{displacementNodeVariableName}.inputs['Midlevel'].default_value = 0.0");
        lines.Add($"{materialVariableName}_links.new({textureVariableName}.outputs['Color'], {displacementNodeVariableName}.inputs['Height'])");
        lines.Add($"{outputNodeVariableName} = {materialVariableName}_nodes['Material Output']");
        lines.Add($"{materialVariableName}_links.new({displacementNodeVariableName}.outputs['Displacement'], {outputNodeVariableName}.inputs['Displacement'])");

        // Enable real displacement. The property location moved across Blender versions
        // (material.displacement_method in 4.1+, material.cycles.displacement_method earlier).
        lines.Add("try:");
        lines.Add($"    {materialVariableName}.displacement_method = 'DISPLACEMENT'");
        lines.Add("except (AttributeError, TypeError):");
        lines.Add("    pass");
        lines.Add("try:");
        lines.Add($"    {materialVariableName}.cycles.displacement_method = 'DISPLACEMENT'");
        lines.Add("except (AttributeError, TypeError):");
        lines.Add("    pass");
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
