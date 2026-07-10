using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Models.Build;
using static OutWit.Controller.Render.Dcc.Services.DccBlenderPythonFormatter;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderNodeEmitter
{
    #region Constants

    private const double MIN_CUSTOM_LIGHT_DISTANCE = 0.01d;

    #endregion

    #region Functions

    public static void AppendMeshNodeLines(List<string> lines, DccSceneBuildInput buildInput, DccBlenderSceneDataWriter dataWriter)
    {
        foreach (var node in buildInput.Scene.Nodes.Where(me => me.Kind == DccNodeKind.Mesh))
        {
            var mesh = buildInput.MeshesById[node.MeshId!];
            var meshVariableName = $"mesh_{SanitizeIdentifier(node.Id)}";
            var objectVariableName = $"object_{SanitizeIdentifier(node.Id)}";
            var trianglesVariableName = $"{meshVariableName}_tris";

            // Bulk geometry goes through the binary sidecar + foreach_set — the numpy/C fast
            // path. Python literals + from_pydata made Blender's parser and per-element RNA
            // loops take ~20 minutes for a 60 MB scene that renders in seconds.
            var vertexCount = mesh.Positions.Count;
            var triangleCount = mesh.TriangleIndices.Count / 3;
            var positionsOffset = dataWriter.AppendVectors3(mesh.Positions);
            var trianglesOffset = dataWriter.AppendInts(mesh.TriangleIndices);

            lines.Add($"{meshVariableName} = bpy.data.meshes.new({ToPythonStringLiteral(mesh.Name)})");
            lines.Add($"{trianglesVariableName} = read_scene_ints({trianglesOffset}, {triangleCount * 3})");
            lines.Add($"{meshVariableName}.vertices.add({vertexCount})");
            lines.Add($"{meshVariableName}.vertices.foreach_set('co', read_scene_floats({positionsOffset}, {vertexCount * 3}))");
            lines.Add($"{meshVariableName}.loops.add({triangleCount * 3})");
            lines.Add($"{meshVariableName}.loops.foreach_set('vertex_index', {trianglesVariableName})");
            lines.Add($"{meshVariableName}.polygons.add({triangleCount})");
            lines.Add($"{meshVariableName}.polygons.foreach_set('loop_start', triangle_loop_starts({triangleCount}))");
            lines.Add($"{meshVariableName}.update(calc_edges=True)");
            AppendMeshNormalLines(lines, mesh, meshVariableName, dataWriter);
            lines.Add($"{objectVariableName} = bpy.data.objects.new({ToPythonStringLiteral(node.Name)}, {meshVariableName})");
            lines.Add($"scene.collection.objects.link({objectVariableName})");
            lines.Add($"set_transform({objectVariableName}, {BuildTranslationTuple(node.LocalTransform)}, {BuildQuaternionTuple(node.LocalTransform)}, {BuildScaleTuple(node.LocalTransform)})");
            lines.Add($"{objectVariableName}.hide_render = {ToPythonBool(!node.Renderable)}");
            lines.Add($"{objectVariableName}.hide_viewport = {ToPythonBool(!node.Visible)}");

            // A backdrop shell (sky dome) is scenery: camera/reflection/refraction rays see it,
            // but it must not act as a giant area light or shadow the scene.
            if (node.IsBackdrop)
            {
                lines.Add($"{objectVariableName}.visible_diffuse = False");
                lines.Add($"{objectVariableName}.visible_shadow = False");
                lines.Add($"{objectVariableName}.visible_volume_scatter = False");
            }

            AppendMeshMaterialLines(lines, buildInput, node, mesh, meshVariableName, dataWriter);

            // Source-application render-only smoothing (e.g. 3ds Max MeshSmooth "Render
            // Iterations") arrives as a subdivision level count instead of baked vertices.
            if (mesh.SubdivisionLevels > 0)
                AppendRenderSubdivisionModifierLines(lines, objectVariableName, mesh.SubdivisionLevels);

            // Give displacement-mapped objects geometry to displace (Cycles true displacement
            // needs subdivided geometry).
            if (MaterialHasDisplacement(buildInput, node))
                AppendSubdivisionModifierLines(lines, objectVariableName);

            AppendMeshUvLayerLines(lines, meshVariableName, trianglesVariableName, "UVMap", "uv_layer", mesh.Uv0, dataWriter);
            AppendMeshUvLayerLines(lines, meshVariableName, trianglesVariableName, "UVMap.001", "uv_layer_1", mesh.Uv1, dataWriter);
            AppendMeshColorLayerLines(lines, meshVariableName, trianglesVariableName, mesh.Colors, dataWriter);
            AppendMeshDeformationLines(lines, objectVariableName, mesh, dataWriter);

            AppendNodeAnimationLines(lines, objectVariableName, node);
            AppendNodeVisibilityAnimationLines(lines, objectVariableName, node);
            lines.Add($"objects_by_node_id[{ToPythonStringLiteral(node.Id)}] = {objectVariableName}");
            lines.Add(string.Empty);
        }
    }

    private static void AppendMeshUvLayerLines(
        List<string> lines,
        string meshVariableName,
        string trianglesVariableName,
        string uvLayerName,
        string variableSuffix,
        List<DccVector2Data> uvs,
        DccBlenderSceneDataWriter dataWriter)
    {
        if (uvs.Count == 0)
            return;

        // The payload UVs are per (unwelded) vertex; gathering them through the triangle index
        // array yields the per-loop layout in one numpy fancy-index instead of a Python loop
        // over every corner. Blender 4.x moved per-loop UVs to the layer's `uv` float2
        // attribute; the `data` accessor stays as the fallback for older builds.
        var layerVariable = $"{meshVariableName}_{variableSuffix}";
        var uvsOffset = dataWriter.AppendVectors2(uvs);
        lines.Add($"{layerVariable} = {meshVariableName}.uv_layers.new(name={ToPythonStringLiteral(uvLayerName)})");
        lines.Add($"{layerVariable}_data = read_scene_floats({uvsOffset}, {uvs.Count * 2}).reshape({uvs.Count}, 2)[{trianglesVariableName}].ravel()");
        lines.Add("try:");
        lines.Add($"    {layerVariable}.uv.foreach_set('vector', {layerVariable}_data)");
        lines.Add("except AttributeError:");
        lines.Add($"    {layerVariable}.data.foreach_set('uv', {layerVariable}_data)");
    }

    private static void AppendMeshDeformationLines(List<string> lines, string objectVariableName, DccMeshData mesh, DccBlenderSceneDataWriter dataWriter)
    {
        if (mesh.DeformationFrames.Count == 0)
            return;

        // Baked vertex-cache deformation as keyframed shape keys: a Basis key (rest pose) plus one
        // key per deformation frame, each keyed 0 -> 1 -> 0 around its frame with LINEAR
        // interpolation. At integer frames exactly one key is active (exact baked pose); between
        // frames adjacent keys cross-fade (weights sum to 1), which both smooths sub-frame motion
        // and gives Cycles real deformation vectors — stepped keys froze geometry inside the
        // shutter window, so motion blur never touched baked deformation (butterfly wings,
        // dragon flaps rendered rigid while the source renderer ghosts them).
        lines.Add($"{objectVariableName}.shape_key_add(name='Basis')");

        var frameIndex = 0;
        foreach (var frame in mesh.DeformationFrames.OrderBy(me => me.Frame))
        {
            var keyVariable = $"{objectVariableName}_shapekey_{frameIndex}";
            var positionsOffset = dataWriter.AppendVectors3(frame.Positions);

            lines.Add($"{keyVariable} = {objectVariableName}.shape_key_add(name='Frame_{frame.Frame}')");
            lines.Add($"{keyVariable}.data.foreach_set('co', read_scene_floats({positionsOffset}, {frame.Positions.Count * 3}))");
            lines.Add($"{keyVariable}.value = 0.0");
            lines.Add($"{keyVariable}.keyframe_insert(data_path='value', frame={frame.Frame - 1})");
            lines.Add($"{keyVariable}.value = 1.0");
            lines.Add($"{keyVariable}.keyframe_insert(data_path='value', frame={frame.Frame})");
            lines.Add($"{keyVariable}.value = 0.0");
            lines.Add($"{keyVariable}.keyframe_insert(data_path='value', frame={frame.Frame + 1})");
            lines.Add($"set_keyframe_interpolation({objectVariableName}.data.shape_keys, {keyVariable}.path_from_id('value'), {frame.Frame - 1}, 'LINEAR')");
            lines.Add($"set_keyframe_interpolation({objectVariableName}.data.shape_keys, {keyVariable}.path_from_id('value'), {frame.Frame}, 'LINEAR')");
            lines.Add($"set_keyframe_interpolation({objectVariableName}.data.shape_keys, {keyVariable}.path_from_id('value'), {frame.Frame + 1}, 'LINEAR')");

            frameIndex++;
        }

        lines.Add(string.Empty);
    }

    private static bool MaterialHasDisplacement(DccSceneBuildInput buildInput, DccNodeData node)
    {
        var mesh = buildInput.MeshesById[node.MeshId!];

        // Per-face materials (multi-material mesh) take precedence over a single node binding, matching
        // AppendMeshMaterialLines - check every material the mesh actually uses for a displacement slot.
        if (mesh.MaterialIndices.Count > 0)
        {
            return mesh.MaterialIndices.Distinct()
                .Select(me => buildInput.Scene.Materials[me])
                .Any(MaterialUsesDisplacement);
        }

        if (string.IsNullOrWhiteSpace(node.MaterialBindingId))
            return false;

        var material = buildInput.Scene.Materials.FirstOrDefault(me => me.Id == node.MaterialBindingId);
        return material != null && MaterialUsesDisplacement(material);
    }

    private static bool MaterialUsesDisplacement(DccMaterialData material)
    {
        return material.TextureSlots.Any(me => me.Slot == DccTextureSlotKind.Displacement);
    }

    private static void AppendSubdivisionModifierLines(List<string> lines, string objectVariableName)
    {
        // The payload mesh is unwelded (per-corner vertices): displacing it without a weld tears
        // every UV-island border open — MoonRock showed the chart boundaries as a grid of square
        // seams over the displaced sphere.
        var weldVariableName = $"{objectVariableName}_displace_weld";
        lines.Add($"{weldVariableName} = {objectVariableName}.modifiers.new(name='WeldBeforeDisplacement', type='WELD')");
        lines.Add($"{weldVariableName}.merge_threshold = 0.0001");

        var modifierVariableName = $"{objectVariableName}_subdiv";
        lines.Add($"{modifierVariableName} = {objectVariableName}.modifiers.new(name='Subdivision', type='SUBSURF')");
        lines.Add($"{modifierVariableName}.subdivision_type = 'SIMPLE'");
        lines.Add($"{modifierVariableName}.levels = 2");
        lines.Add($"{modifierVariableName}.render_levels = 4");
    }

    private static void AppendRenderSubdivisionModifierLines(List<string> lines, string objectVariableName, int subdivisionLevels)
    {
        // The payload mesh is unwelded (one vertex per face corner, for exact custom normals), and
        // Catmull-Clark on disconnected triangles shrinks each one into a separate patch — weld the
        // coincident duplicates back together first. The threshold only has to catch exact
        // duplicates, so it stays far below any real edge length.
        var weldVariableName = $"{objectVariableName}_weld";
        lines.Add($"{weldVariableName} = {objectVariableName}.modifiers.new(name='WeldBeforeSubdivision', type='WELD')");
        lines.Add($"{weldVariableName}.merge_threshold = 0.0001");

        var modifierVariableName = $"{objectVariableName}_render_subdiv";
        lines.Add($"{modifierVariableName} = {objectVariableName}.modifiers.new(name='RenderSubdivision', type='SUBSURF')");
        lines.Add($"{modifierVariableName}.subdivision_type = 'CATMULL_CLARK'");
        lines.Add($"{modifierVariableName}.levels = {subdivisionLevels}");
        lines.Add($"{modifierVariableName}.render_levels = {subdivisionLevels}");
    }

    private static void AppendMeshColorLayerLines(
        List<string> lines,
        string meshVariableName,
        string trianglesVariableName,
        List<DccColorData> colors,
        DccBlenderSceneDataWriter dataWriter)
    {
        if (colors.Count == 0)
            return;

        // Per-corner vertex colours as a BYTE_COLOR attribute (the conventional vertex-colour
        // type); the per-vertex payload gathers to per-loop through the triangle index array.
        var layerVariable = $"{meshVariableName}_color_layer";
        var colorsOffset = dataWriter.AppendColors(colors);
        lines.Add($"{layerVariable} = {meshVariableName}.color_attributes.new(name='Color', type='BYTE_COLOR', domain='CORNER')");
        lines.Add($"{layerVariable}.data.foreach_set('color', read_scene_floats({colorsOffset}, {colors.Count * 4}).reshape({colors.Count}, 4)[{trianglesVariableName}].ravel())");
    }

    private static void AppendMeshNormalLines(List<string> lines, DccMeshData mesh, string meshVariableName, DccBlenderSceneDataWriter dataWriter)
    {
        // The DCC payload carries per-vertex normals (the exporter resolves them from the source
        // smoothing groups). The mesh vertices are unwelded — one vertex per face corner — so a
        // per-vertex custom-normal set reproduces the source hard/soft edges exactly. Without this
        // Blender recomputes flat face normals and every curved surface renders faceted.
        if (mesh.Normals.Count == 0 || mesh.Normals.Count != mesh.Positions.Count)
            return;

        var normalsOffset = dataWriter.AppendVectors3(mesh.Normals);
        lines.Add($"{meshVariableName}.polygons.foreach_set('use_smooth', np.ones(len({meshVariableName}.polygons), dtype=bool))");
        lines.Add($"{meshVariableName}.normals_split_custom_set_from_vertices(read_scene_floats({normalsOffset}, {mesh.Normals.Count * 3}).reshape({mesh.Normals.Count}, 3))");
    }

    public static void AppendLightNodeLines(List<string> lines, DccSceneBuildInput buildInput)
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
            {
                lines.Add($"{lightVariableName}.spot_size = math.radians({FormatDouble(light.SpotAngleDegrees)})");

                // Source hotspot/falloff cone difference: 0 keeps the legacy hard edge.
                if (light.SpotBlend > 0d)
                    lines.Add($"{lightVariableName}.spot_blend = {FormatDouble(Math.Clamp(light.SpotBlend, 0d, 1d))}");
            }

            if (light.Kind == DccLightKind.Area)
            {
                lines.Add($"{lightVariableName}.shape = 'RECTANGLE'");
                lines.Add($"{lightVariableName}.size = {FormatDouble(light.AreaWidth > 0d ? light.AreaWidth : 1d)}");
                lines.Add($"{lightVariableName}.size_y = {FormatDouble(light.AreaHeight > 0d ? light.AreaHeight : 1d)}");
            }

            lines.Add($"{lightVariableName}.use_shadow = {ToPythonBool(light.CastShadows)}");

            // 3ds Max standard lights default to NO distance decay, while Cycles lights are
            // physically inverse-square — near surfaces blow out and far ones go black (interior
            // walls / the dragon's far omnis). The Light Falloff node's Constant output cancels
            // the physical falloff so the received intensity matches the source renderer.
            if (light.NoDecay && light.Kind is DccLightKind.Point or DccLightKind.Spot)
            {
                // The Light Falloff node's Constant output removes the physical inverse-square
                // falloff in CYCLES light shaders (verified: identical illumination at 5 and 20
                // units). Ray-Length-based compensation does NOT work — Ray Length evaluates to
                // zero inside light shaders. With use_nodes the light's energy is ignored, so the
                // falloff node carries the light's power (received irradiance ≈ Strength / 4π).
                // Energy interacts unpredictably with light node trees — pin it to 1 W so the
                // falloff strength is the single source of the light's power. Shadows must stay ON:
                // in Cycles 5.1 a light with use_shadow=False AND a node tree emits NOTHING (the
                // shadow-less fast path skips node evaluation), so the authored shadows-off flag is
                // overridden for no-decay lights.
                lines.Add($"{lightVariableName}.energy = 1.0");
                lines.Add($"{lightVariableName}.use_shadow = True");
                lines.Add($"{lightVariableName}.use_nodes = True");
                lines.Add($"{lightVariableName}_falloff = {lightVariableName}.node_tree.nodes.new('ShaderNodeLightFalloff')");
                lines.Add($"{lightVariableName}_falloff.inputs['Strength'].default_value = {FormatDouble(light.Intensity)}");
                lines.Add($"{lightVariableName}.node_tree.links.new({lightVariableName}_falloff.outputs['Constant'], {lightVariableName}.node_tree.nodes['Emission'].inputs['Strength'])");
            }

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

    public static void AppendCameraNodeLines(List<string> lines, DccSceneBuildInput buildInput)
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

            if (camera.EnableDepthOfField && camera.FocusDistance > 0d)
            {
                lines.Add($"{cameraVariableName}.dof.use_dof = True");
                lines.Add($"{cameraVariableName}.dof.focus_distance = {FormatDouble(camera.FocusDistance)}");
                lines.Add($"{cameraVariableName}.dof.aperture_fstop = {FormatDouble(camera.FStop > 0d ? camera.FStop : 2.8d)}");
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

    private static void AppendMeshMaterialLines(
        List<string> lines,
        DccSceneBuildInput buildInput,
        DccNodeData node,
        DccMeshData mesh,
        string meshVariableName,
        DccBlenderSceneDataWriter dataWriter)
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

            // One local index per triangle through the sidecar — the old per-polygon assignment
            // emitted a script line (and an RNA lookup) per triangle.
            var localMaterialIndices = mesh.MaterialIndices.Select(me => localMaterialIndexBySceneIndex[me]).ToList();
            var indicesOffset = dataWriter.AppendInts(localMaterialIndices);
            lines.Add($"{meshVariableName}.polygons.foreach_set('material_index', read_scene_ints({indicesOffset}, {localMaterialIndices.Count}))");

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

    #endregion
}
