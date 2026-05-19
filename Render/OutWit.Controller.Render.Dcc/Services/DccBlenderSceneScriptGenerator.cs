using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Models.Build;
using static OutWit.Controller.Render.Dcc.Services.DccBlenderPythonFormatter;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderSceneScriptGenerator
{
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
        DccBlenderMaterialEmitter.AppendMaterialLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendMeshNodeLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendCameraNodeLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendLightNodeLines(lines, buildInput);
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

    private static string ResolveImagePath(DccSceneBuildInput buildInput, DccImageAssetData imageAsset)
    {
        if (buildInput.ImageAttachmentsByImageId.TryGetValue(imageAsset.Id, out var attachment))
            return attachment.RelativePath;

        if (!string.IsNullOrWhiteSpace(imageAsset.RelativePath))
            return imageAsset.RelativePath;

        return imageAsset.SourcePath;
    }

    #endregion
}
