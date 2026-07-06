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

        AppendColorManagementLines(lines, buildInput);
        AppendMotionBlurLines(lines, buildInput);
        AppendWorldLines(lines, buildInput);
        AppendImageLines(lines, buildInput);
        DccBlenderMaterialEmitter.AppendMaterialLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendMeshNodeLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendCameraNodeLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendLightNodeLines(lines, buildInput);
        AppendParentingLines(lines, buildInput);
        AppendSceneCameraLine(lines, buildInput);

        return string.Join("\n", lines);
    }

    private static void AppendColorManagementLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        var renderSettings = buildInput.Scene.RenderSettings;

        // Only touch color management when the scene specifies it, so renders without it keep the
        // generator/Blender default view transform.
        if (!string.IsNullOrWhiteSpace(renderSettings.ViewTransform))
            lines.Add($"scene.view_settings.view_transform = {ToPythonStringLiteral(renderSettings.ViewTransform)}");

        if (renderSettings.Exposure != 0d)
            lines.Add($"scene.view_settings.exposure = {FormatDouble(renderSettings.Exposure)}");
    }

    private static void AppendMotionBlurLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        var renderSettings = buildInput.Scene.RenderSettings;

        // Only touch motion blur when the scene enables it, so default renders are unchanged.
        if (!renderSettings.MotionBlur)
            return;

        lines.Add("scene.render.use_motion_blur = True");
        lines.Add($"scene.render.motion_blur_shutter = {FormatDouble(renderSettings.MotionBlurShutter)}");
    }

    private static void AppendWorldLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        var world = buildInput.Scene.World;
        if (world == null)
            return;

        // Environment (HDRI) world: an equirectangular image drives the background and ambient
        // light. Takes precedence over the constant background colour when an image is referenced.
        var environmentImage = string.IsNullOrWhiteSpace(world.EnvironmentImageId)
            ? null
            : buildInput.Scene.ImageAssets.FirstOrDefault(me => me.Id == world.EnvironmentImageId);

        if (environmentImage != null)
        {
            AppendEnvironmentWorldLines(lines, buildInput, world, environmentImage);
            return;
        }

        var color = world.BackgroundColor;

        // Keep the empty-world behaviour (no ambient / black background) when the world is a pure
        // black at full strength — that is indistinguishable from no world for rendering and keeps
        // existing scenes unchanged. Only build a world node tree when it actually contributes.
        if (color.R <= 0d && color.G <= 0d && color.B <= 0d)
            return;

        // A source-application background COLOUR is a backdrop, not a light source (3ds Max never
        // lights the scene with it), but a plain Cycles world colour is full ambient — it washed
        // every open-air scene toward the backdrop colour. Restrict the colour to camera and
        // reflection/refraction rays (Max shows the backdrop in mirrors and glass too); diffuse
        // rays see black. Environment IMAGES keep lighting the scene (HDRI path above).
        lines.Add("scene_world = bpy.data.worlds.new('World')");
        lines.Add("scene.world = scene_world");
        lines.Add("scene_world.use_nodes = True");
        lines.Add("scene_world_tree = scene_world.node_tree");
        lines.Add("scene_world_background = scene_world_tree.nodes['Background']");
        lines.Add($"scene_world_background.inputs['Color'].default_value = ({FormatDouble(color.R)}, {FormatDouble(color.G)}, {FormatDouble(color.B)}, 1.0)");
        lines.Add($"scene_world_background.inputs['Strength'].default_value = {FormatDouble(world.Strength)}");
        lines.Add("scene_world_dark = scene_world_tree.nodes.new('ShaderNodeBackground')");
        lines.Add("scene_world_dark.inputs['Color'].default_value = (0.0, 0.0, 0.0, 1.0)");
        lines.Add("scene_world_light_path = scene_world_tree.nodes.new('ShaderNodeLightPath')");
        lines.Add("scene_world_visible_fac = scene_world_tree.nodes.new('ShaderNodeMath')");
        lines.Add("scene_world_visible_fac.operation = 'MAXIMUM'");
        lines.Add("scene_world_visible_fac_2 = scene_world_tree.nodes.new('ShaderNodeMath')");
        lines.Add("scene_world_visible_fac_2.operation = 'MAXIMUM'");
        lines.Add("scene_world_mix = scene_world_tree.nodes.new('ShaderNodeMixShader')");
        lines.Add("scene_world_tree.links.new(scene_world_light_path.outputs['Is Camera Ray'], scene_world_visible_fac.inputs[0])");
        lines.Add("scene_world_tree.links.new(scene_world_light_path.outputs['Is Glossy Ray'], scene_world_visible_fac.inputs[1])");
        lines.Add("scene_world_tree.links.new(scene_world_visible_fac.outputs['Value'], scene_world_visible_fac_2.inputs[0])");
        lines.Add("scene_world_tree.links.new(scene_world_light_path.outputs['Is Transmission Ray'], scene_world_visible_fac_2.inputs[1])");
        lines.Add("scene_world_tree.links.new(scene_world_visible_fac_2.outputs['Value'], scene_world_mix.inputs['Fac'])");
        lines.Add("scene_world_tree.links.new(scene_world_dark.outputs['Background'], scene_world_mix.inputs[1])");
        lines.Add("scene_world_tree.links.new(scene_world_background.outputs['Background'], scene_world_mix.inputs[2])");
        lines.Add("scene_world_tree.links.new(scene_world_mix.outputs['Shader'], scene_world_tree.nodes['World Output'].inputs['Surface'])");
        lines.Add(string.Empty);
    }

    private static void AppendEnvironmentWorldLines(List<string> lines, DccSceneBuildInput buildInput, DccWorldData world, DccImageAssetData environmentImage)
    {
        var imagePath = ResolveImagePath(buildInput, environmentImage);

        lines.Add("scene_world = bpy.data.worlds.new('World')");
        lines.Add("scene.world = scene_world");
        lines.Add("scene_world.use_nodes = True");
        lines.Add("world_node_tree = scene_world.node_tree");
        lines.Add("world_background = world_node_tree.nodes['Background']");
        lines.Add("world_environment = world_node_tree.nodes.new('ShaderNodeTexEnvironment')");
        lines.Add($"world_environment.image = bpy.data.images.load({ToPythonStringLiteral(imagePath)}, check_existing=True)");
        lines.Add("world_node_tree.links.new(world_environment.outputs['Color'], world_background.inputs['Color'])");
        lines.Add($"world_background.inputs['Strength'].default_value = {FormatDouble(world.Strength)}");

        // Optional orientation of the environment image around the up axis.
        if (Math.Abs(world.EnvironmentRotationDegrees) > 1e-6)
        {
            var radians = world.EnvironmentRotationDegrees * Math.PI / 180d;
            lines.Add("world_tex_coord = world_node_tree.nodes.new('ShaderNodeTexCoord')");
            lines.Add("world_mapping = world_node_tree.nodes.new('ShaderNodeMapping')");
            lines.Add($"world_mapping.inputs['Rotation'].default_value[2] = {FormatDouble(radians)}");
            lines.Add("world_node_tree.links.new(world_tex_coord.outputs['Generated'], world_mapping.inputs['Vector'])");
            lines.Add("world_node_tree.links.new(world_mapping.outputs['Vector'], world_environment.inputs['Vector'])");
        }

        lines.Add(string.Empty);
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
