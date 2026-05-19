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

    public static void AppendMeshNodeLines(List<string> lines, DccSceneBuildInput buildInput)
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
