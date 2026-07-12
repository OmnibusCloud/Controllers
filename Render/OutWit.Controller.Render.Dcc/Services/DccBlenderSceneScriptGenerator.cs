using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Models.Build;
using static OutWit.Controller.Render.Dcc.Services.DccBlenderPythonFormatter;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderSceneScriptGenerator
{
    #region Functions

    public static DccBlenderSceneScript Create(DccSceneBuildInput buildInput)
    {
        var lines = new List<string>
        {
            "import bpy",
            "import math",
            "import mathutils",
            "import os",
            "import numpy as np",
            "",
            // Bulk mesh data (positions/indices/normals/UVs/colours/shape keys) lives in a binary
            // sidecar next to this script and loads through numpy + foreach_set — the C fast path.
            // Emitting it as Python literals made the script parser take ~20 minutes for a scene
            // that renders in seconds.
            "scene_data_file = None",
            $"scene_data_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '{DccBlenderSceneDataWriter.FILE_NAME}')",
            "if os.path.exists(scene_data_path):",
            "    scene_data_file = open(scene_data_path, 'rb')",
            "",
            "def read_scene_floats(offset, count):",
            "    scene_data_file.seek(offset)",
            "    return np.fromfile(scene_data_file, dtype='<f4', count=count)",
            "",
            "def read_scene_ints(offset, count):",
            "    scene_data_file.seek(offset)",
            "    return np.fromfile(scene_data_file, dtype='<i4', count=count)",
            "",
            "def triangle_loop_starts(triangle_count):",
            "    return np.arange(0, triangle_count * 3, 3, dtype=np.int32)",
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
            "def action_fcurves(action):",
            "    # Blender 4.4+ slotted actions dropped Action.fcurves; walk layers/strips/channelbags",
            "    # there. The legacy accessor stays for older builds.",
            "    if hasattr(action, 'fcurves'):",
            "        return list(action.fcurves)",
            "    curves = []",
            "    for layer in action.layers:",
            "        for strip in layer.strips:",
            "            for bag in strip.channelbags:",
            "                curves.extend(bag.fcurves)",
            "    return curves",
            "",
            "def set_keyframe_interpolation(obj, data_path, frame, interpolation):",
            "    if obj.animation_data is None or obj.animation_data.action is None:",
            "        return",
            "    for fcurve in action_fcurves(obj.animation_data.action):",
            "        if fcurve.data_path != data_path:",
            "            continue",
            "        for keyframe in fcurve.keyframe_points:",
            "            if int(round(keyframe.co.x)) == frame:",
            "                keyframe.interpolation = interpolation",
            ""
        };

        var dataWriter = new DccBlenderSceneDataWriter();

        AppendColorManagementLines(lines, buildInput);
        AppendGlobalIlluminationLines(lines, buildInput);
        AppendMotionBlurLines(lines, buildInput);
        AppendWorldLines(lines, buildInput);
        AppendImageLines(lines, buildInput);
        DccBlenderMaterialEmitter.AppendMaterialLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendMeshNodeLines(lines, buildInput, dataWriter);
        DccBlenderNodeEmitter.AppendCameraNodeLines(lines, buildInput);
        DccBlenderNodeEmitter.AppendLightNodeLines(lines, buildInput);
        AppendParentingLines(lines, buildInput);
        AppendSceneCameraLine(lines, buildInput);

        return new DccBlenderSceneScript
        {
            PythonScript = string.Join("\n", lines),
            SceneData = dataWriter.ToArray()
        };
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

    private static void AppendGlobalIlluminationLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        // A no-GI source renderer lights surfaces with DIRECT light only; Cycles' diffuse bounces
        // add indirect light everywhere and systematically brighten enclosed scenes. Glossy and
        // transmission bounces stay — the source class does trace reflections and refractions.
        if (!buildInput.Scene.RenderSettings.NoGlobalIllumination)
            return;

        lines.Add("try:");
        lines.Add("    scene.cycles.diffuse_bounces = 0");
        lines.Add("except AttributeError:");
        lines.Add("    pass");
    }

    private static void AppendMotionBlurLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        var renderSettings = buildInput.Scene.RenderSettings;

        // Only touch motion blur when the scene enables it, so default renders are unchanged.
        if (!renderSettings.MotionBlur)
            return;

        if (renderSettings.ImageMotionBlur)
        {
            // The source blur is an IMAGE-space post smear (a sharp frame with a velocity smear
            // layered on top — 3ds Max "Image Motion Blur"). Shutter-integrated blur turns a
            // flapping butterfly into an unreadable smudge; the compositor's Vector Blur is the
            // same algorithm class as the source effect and keeps the subject crisp underneath.
            lines.Add("scene.render.use_motion_blur = False");
            lines.Add("view_layer = scene.view_layers[0]");
            lines.Add("view_layer.use_pass_vector = True");
            lines.Add("view_layer.use_pass_z = True");
            // Blender 5.x moved the compositor to a node-group asset (scene.compositing_node_group,
            // NodeGroupOutput, Samples/Shutter as sockets); older builds keep scene.node_tree.
            lines.Add("try:");
            lines.Add("    compositor_tree = bpy.data.node_groups.new('DccImageMotionBlur', 'CompositorNodeTree')");
            lines.Add("    scene.compositing_node_group = compositor_tree");
            lines.Add("    compositor_output_kind = 'GROUP'");
            lines.Add("except (AttributeError, TypeError):");
            lines.Add("    scene.use_nodes = True");
            lines.Add("    compositor_tree = scene.node_tree");
            lines.Add("    compositor_output_kind = 'COMPOSITE'");
            lines.Add("compositor_nodes = compositor_tree.nodes");
            lines.Add("compositor_links = compositor_tree.links");
            lines.Add("compositor_nodes.clear()");
            lines.Add("compositor_render_layers = compositor_nodes.new('CompositorNodeRLayers')");
            lines.Add("compositor_vector_blur = compositor_nodes.new('CompositorNodeVecBlur')");
            lines.Add("try:");
            lines.Add("    compositor_vector_blur.inputs['Samples'].default_value = 32");
            lines.Add($"    compositor_vector_blur.inputs['Shutter'].default_value = {FormatDouble(renderSettings.MotionBlurShutter)}");
            lines.Add("except KeyError:");
            lines.Add("    compositor_vector_blur.samples = 32");
            lines.Add($"    compositor_vector_blur.factor = {FormatDouble(renderSettings.MotionBlurShutter)}");
            lines.Add("if compositor_output_kind == 'GROUP':");
            lines.Add("    compositor_tree.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')");
            lines.Add("    compositor_output = compositor_nodes.new('NodeGroupOutput')");
            lines.Add("else:");
            lines.Add("    compositor_output = compositor_nodes.new('CompositorNodeComposite')");
            lines.Add("compositor_links.new(compositor_render_layers.outputs['Image'], compositor_vector_blur.inputs['Image'])");
            lines.Add("try:");
            lines.Add("    compositor_links.new(compositor_render_layers.outputs['Depth'], compositor_vector_blur.inputs['Depth'])");
            lines.Add("except KeyError:");
            lines.Add("    compositor_links.new(compositor_render_layers.outputs['Depth'], compositor_vector_blur.inputs['Z'])");
            lines.Add("compositor_links.new(compositor_render_layers.outputs['Vector'], compositor_vector_blur.inputs['Speed'])");
            lines.Add("compositor_links.new(compositor_vector_blur.outputs['Image'], compositor_output.inputs['Image'])");
            return;
        }

        lines.Add("scene.render.use_motion_blur = True");
        lines.Add($"scene.render.motion_blur_shutter = {FormatDouble(renderSettings.MotionBlurShutter)}");

        // Open the shutter AT the frame, not centered on it: a centered window looks backwards
        // across camera-cut keyframes (montage cuts held with CONSTANT interpolation) and ghosts
        // the previous shot over the frame — troll_cleric's whole close-up smeared into a wash.
        // A start-positioned shutter only ever integrates forward inside the held key.
        lines.Add("scene.render.motion_blur_position = 'START'");
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

        if (world.EnvironmentIsScreenMapped)
        {
            AppendScreenBackdropWorldLines(lines, world, imagePath);
            return;
        }

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

    // A screen-mapped environment is a 2D backdrop the source application stretches across the
    // render window, not a panorama — sample it with window coordinates so the camera sees the
    // image exactly as authored (an equirect wrap would bury the picture at the zenith and show
    // the camera nothing but the seam colour). Like the constant-colour world, the backdrop is a
    // picture, not a light source: camera, reflection and refraction rays see it (the source
    // renderer shows the backdrop in mirrors and glass too), diffuse rays see black.
    private static void AppendScreenBackdropWorldLines(List<string> lines, DccWorldData world, string imagePath)
    {
        lines.Add("scene_world = bpy.data.worlds.new('World')");
        lines.Add("scene.world = scene_world");
        lines.Add("scene_world.use_nodes = True");
        lines.Add("world_node_tree = scene_world.node_tree");
        lines.Add("world_background = world_node_tree.nodes['Background']");
        lines.Add("world_backdrop = world_node_tree.nodes.new('ShaderNodeTexImage')");
        lines.Add($"world_backdrop.image = bpy.data.images.load({ToPythonStringLiteral(imagePath)}, check_existing=True)");
        lines.Add("world_backdrop.extension = 'EXTEND'");
        lines.Add("world_tex_coord = world_node_tree.nodes.new('ShaderNodeTexCoord')");
        lines.Add("world_node_tree.links.new(world_tex_coord.outputs['Window'], world_backdrop.inputs['Vector'])");
        lines.Add("world_node_tree.links.new(world_backdrop.outputs['Color'], world_background.inputs['Color'])");
        lines.Add($"world_background.inputs['Strength'].default_value = {FormatDouble(world.Strength)}");
        // Reflection and refraction rays cannot use window coordinates — a mirror's reflected
        // ray projects outside the frame and sampled the EXTEND border (C03's chrome spheres
        // reflected darkness instead of the photo backdrop). Give those rays the same image
        // wrapped over the ray direction (an equirect approximation of Scanline showing the
        // backdrop in mirrors and glass). Diffuse rays keep seeing black — the backdrop is a
        // picture, not a light source.
        lines.Add("world_backdrop_reflected = world_node_tree.nodes.new('ShaderNodeTexEnvironment')");
        lines.Add($"world_backdrop_reflected.image = bpy.data.images.load({ToPythonStringLiteral(imagePath)}, check_existing=True)");
        lines.Add("world_background_reflected = world_node_tree.nodes.new('ShaderNodeBackground')");
        lines.Add("world_node_tree.links.new(world_backdrop_reflected.outputs['Color'], world_background_reflected.inputs['Color'])");
        lines.Add($"world_background_reflected.inputs['Strength'].default_value = {FormatDouble(world.Strength)}");
        lines.Add("scene_world_dark = world_node_tree.nodes.new('ShaderNodeBackground')");
        lines.Add("scene_world_dark.inputs['Color'].default_value = (0.0, 0.0, 0.0, 1.0)");
        lines.Add("scene_world_light_path = world_node_tree.nodes.new('ShaderNodeLightPath')");
        lines.Add("scene_world_reflect_fac = world_node_tree.nodes.new('ShaderNodeMath')");
        lines.Add("scene_world_reflect_fac.operation = 'MAXIMUM'");
        lines.Add("scene_world_reflect_mix = world_node_tree.nodes.new('ShaderNodeMixShader')");
        lines.Add("scene_world_camera_mix = world_node_tree.nodes.new('ShaderNodeMixShader')");
        lines.Add("world_node_tree.links.new(scene_world_light_path.outputs['Is Glossy Ray'], scene_world_reflect_fac.inputs[0])");
        lines.Add("world_node_tree.links.new(scene_world_light_path.outputs['Is Transmission Ray'], scene_world_reflect_fac.inputs[1])");
        lines.Add("world_node_tree.links.new(scene_world_reflect_fac.outputs['Value'], scene_world_reflect_mix.inputs['Fac'])");
        lines.Add("world_node_tree.links.new(scene_world_dark.outputs['Background'], scene_world_reflect_mix.inputs[1])");
        lines.Add("world_node_tree.links.new(world_background_reflected.outputs['Background'], scene_world_reflect_mix.inputs[2])");
        lines.Add("world_node_tree.links.new(scene_world_light_path.outputs['Is Camera Ray'], scene_world_camera_mix.inputs['Fac'])");
        lines.Add("world_node_tree.links.new(scene_world_reflect_mix.outputs['Shader'], scene_world_camera_mix.inputs[1])");
        lines.Add("world_node_tree.links.new(world_background.outputs['Background'], scene_world_camera_mix.inputs[2])");
        lines.Add("world_node_tree.links.new(scene_world_camera_mix.outputs['Shader'], world_node_tree.nodes['World Output'].inputs['Surface'])");
        lines.Add(string.Empty);
    }

    private static void AppendImageLines(List<string> lines, DccSceneBuildInput buildInput)
    {
        var emitted = false;
        foreach (var imageAsset in buildInput.Scene.ImageAssets)
        {
            // Only load images the server actually materialized as an attachment under the work
            // directory. An image asset with no matching attachment carries only its original
            // client-machine path (SourcePath/RelativePath), which is untrusted scene data — loading
            // it verbatim would read an arbitrary server file (or force an outbound UNC connection)
            // and pack_all() would embed it into the returned .blend. Referenced images (material
            // slots, world environment) are guaranteed an attachment by the contract validator, so
            // skipping the unattached remainder — always unreferenced — never drops a used texture.
            if (!buildInput.ImageAttachmentsByImageId.ContainsKey(imageAsset.Id))
                continue;

            var imageVariableName = $"image_{SanitizeIdentifier(imageAsset.Id)}";
            var imagePath = ResolveImagePath(buildInput, imageAsset);

            lines.Add($"{imageVariableName} = bpy.data.images.load({ToPythonStringLiteral(imagePath)}, check_existing=True)");
            lines.Add($"images_by_id[{ToPythonStringLiteral(imageAsset.Id)}] = {imageVariableName}");
            emitted = true;
        }

        if (emitted)
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
        // The only path we may hand to bpy.data.images.load is a materialized attachment: its
        // RelativePath is absolutized under (and containment-checked against) the work directory
        // during MaterializeAttachments/CreateScriptBuildInput, so a malicious scene cannot point
        // the load at an arbitrary host file. The raw SourcePath/RelativePath are untrusted client
        // paths and must never reach a load. Every caller guarantees an attachment (referenced
        // images are validated to have one; the image-load loop skips those without) — an absent
        // attachment here is a contract violation, not a fallback.
        if (buildInput.ImageAttachmentsByImageId.TryGetValue(imageAsset.Id, out var attachment))
            return attachment.RelativePath;

        throw new InvalidOperationException(
            $"Render.BuildBlendFromDccScene image asset '{imageAsset.Id}' has no materialized attachment; refusing to load an untrusted client path.");
    }

    #endregion
}
