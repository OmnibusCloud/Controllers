using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Controller.Render.Dcc.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Tests.Services;

/// <summary>
/// Keyframe/animation-emission tests for DccBlenderSceneScriptGenerator —
/// material keyframes (base color, opacity, metallic, roughness, normal
/// strength, alpha-clip threshold, UV transform), node/camera/light transform
/// + visibility keyframes, camera FOV/clip + light intensity/color/range/spot
/// keyframes. The static configuration tests live in
/// <see cref="DccBlenderSceneScriptGeneratorTests"/>.
/// </summary>
[TestFixture]
public sealed class DccBlenderSceneScriptGeneratorAnimationTests
{
    #region Tests

    [Test]
    public void CreateEmitsMaterialNormalStrengthKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateNormalImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Normal,
            ImageAssetId = "image:normal"
        });
        var keyframe = DccRenderTestData.CreateMaterialNormalStrengthKeyframe(2, 2d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].NormalStrengthKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("normal_strength_socket_material_material_cube = normal_map_material_material_cube.inputs['Strength']"));
            Assert.That(script, Does.Contain("normal_strength_socket_material_material_cube.default_value = 2.0"));
            Assert.That(script, Does.Contain("normal_strength_socket_material_material_cube.keyframe_insert(data_path='default_value', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube.node_tree, normal_strength_path_material_material_cube, 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsMaterialAlphaClipThresholdKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;
        scene.Materials[0].Opacity = 0.5d;
        var keyframe = DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(2, 0.2d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].AlphaClipThresholdKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("material_material_cube.alpha_threshold = 0.5"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("material_material_cube.alpha_threshold = 0.2"));
            Assert.That(script, Does.Contain("material_material_cube.keyframe_insert(data_path='alpha_threshold', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube, 'alpha_threshold', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsMaterialOpacityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateMaterialOpacityKeyframe(2, 0.5d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].OpacityKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("opacity_value_material_material_cube = material_material_cube_nodes.new('ShaderNodeValue')"));
            Assert.That(script, Does.Contain("opacity_socket_material_material_cube.default_value = 0.5"));
            Assert.That(script, Does.Contain("opacity_socket_material_material_cube.keyframe_insert(data_path='default_value', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube.node_tree, opacity_path_material_material_cube, 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsMaterialBaseColorKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateMaterialBaseColorKeyframe(2, 0.2d, 0.3d, 1d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].BaseColorKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("base_color_value_material_material_cube = material_material_cube_nodes.new('ShaderNodeRGB')"));
            Assert.That(script, Does.Contain("base_color_socket_material_material_cube.default_value = (0.2, 0.3, 1.0, 1.0)"));
            Assert.That(script, Does.Contain("base_color_socket_material_material_cube.keyframe_insert(data_path='default_value', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube.node_tree, base_color_path_material_material_cube, 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsMaterialMetallicKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateMaterialMetallicKeyframe(2, 0.8d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].MetallicKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("metallic_value_material_material_cube = material_material_cube_nodes.new('ShaderNodeValue')"));
            Assert.That(script, Does.Contain("metallic_socket_material_material_cube.default_value = 0.8"));
            Assert.That(script, Does.Contain("metallic_socket_material_material_cube.keyframe_insert(data_path='default_value', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube.node_tree, metallic_path_material_material_cube, 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsMaterialRoughnessKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateMaterialRoughnessKeyframe(2, 0.2d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].RoughnessKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("roughness_value_material_material_cube = material_material_cube_nodes.new('ShaderNodeValue')"));
            Assert.That(script, Does.Contain("roughness_socket_material_material_cube.default_value = 0.2"));
            Assert.That(script, Does.Contain("roughness_socket_material_material_cube.keyframe_insert(data_path='default_value', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube.node_tree, roughness_path_material_material_cube, 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsUvTransformKeyframesForTextureSlotTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateTextureTransformKeyframe(2, 2d, 0.5d, 0.25d, -0.1d, 45d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].TextureSlots[0].UvTransformKeyframes = [keyframe];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color_location = mapping_material_material_cube_base_color.inputs['Location']"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color_location.default_value[0] = 0.25"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color_rotation.default_value[2] = math.radians(45.0)"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color_scale.default_value[0] = 2.0"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color_location.keyframe_insert(data_path='default_value', frame=2, index=0)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(material_material_cube.node_tree, mapping_material_material_cube_base_color_location_path, 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsNodeTransformKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateTransformKeyframe(2, 3d, 4d, 5d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Nodes[0].TransformKeyframes =
        [
            keyframe
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("set_transform(object_node_cube, (3.0, 4.0, 5.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0))"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='location', frame=2)"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='rotation_quaternion', frame=2)"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='scale', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(object_node_cube, 'location', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsCameraTransformKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Nodes[^1].TransformKeyframes =
        [
            DccRenderTestData.CreateTransformKeyframe(2, 6d, -4d, 3d)
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("set_transform(object_node_camera_main, (6.0, -4.0, 3.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0))"));
            Assert.That(script, Does.Contain("object_node_camera_main.keyframe_insert(data_path='location', frame=2)"));
            Assert.That(script, Does.Contain("object_node_camera_main.keyframe_insert(data_path='rotation_quaternion', frame=2)"));
        });
    }

    [Test]
    public void CreateEmitsCameraFovKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var camera = DccRenderTestData.CreateCamera();
        var keyframe = DccRenderTestData.CreateCameraFovKeyframe(2, 60d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        camera.VerticalFovKeyframes = [keyframe];
        scene.Cameras.Add(camera);
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("set_camera_vertical_fov(camera_node_camera_main, 45.0)"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("set_camera_vertical_fov(camera_node_camera_main, 60.0)"));
            Assert.That(script, Does.Contain("camera_node_camera_main.keyframe_insert(data_path='lens', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(camera_node_camera_main, 'lens', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsCameraClipKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var camera = DccRenderTestData.CreateCamera();
        var nearKeyframe = DccRenderTestData.CreateCameraNearClipKeyframe(2, 0.5d);
        nearKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var farKeyframe = DccRenderTestData.CreateCameraFarClipKeyframe(2, 600d);
        farKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        camera.NearClipKeyframes = [nearKeyframe];
        camera.FarClipKeyframes = [farKeyframe];
        scene.Cameras.Add(camera);
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("camera_node_camera_main.clip_start = 0.1"));
            Assert.That(script, Does.Contain("camera_node_camera_main.clip_end = 500.0"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("camera_node_camera_main.clip_start = 0.5"));
            Assert.That(script, Does.Contain("camera_node_camera_main.keyframe_insert(data_path='clip_start', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(camera_node_camera_main, 'clip_start', 2, 'LINEAR')"));
            Assert.That(script, Does.Contain("camera_node_camera_main.clip_end = 600.0"));
            Assert.That(script, Does.Contain("camera_node_camera_main.keyframe_insert(data_path='clip_end', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(camera_node_camera_main, 'clip_end', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsLightTransformKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Nodes[^1].TransformKeyframes =
        [
            DccRenderTestData.CreateTransformKeyframe(2, 5d, -3d, 7d)
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("set_transform(object_node_key_light, (5.0, -3.0, 7.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0))"));
            Assert.That(script, Does.Contain("object_node_key_light.keyframe_insert(data_path='location', frame=2)"));
            Assert.That(script, Does.Contain("object_node_key_light.keyframe_insert(data_path='scale', frame=2)"));
        });
    }

    [Test]
    public void CreateEmitsLightIntensityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateLight();
        var intensityKeyframe = DccRenderTestData.CreateLightIntensityKeyframe(2, 400d);
        intensityKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        light.IntensityKeyframes = [intensityKeyframe];
        scene.Lights.Add(light);
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_key_light.energy = 1200.0"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("light_node_key_light.energy = 400.0"));
            Assert.That(script, Does.Contain("light_node_key_light.keyframe_insert(data_path='energy', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(light_node_key_light, 'energy', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsLightColorKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateLight();
        var colorKeyframe = DccRenderTestData.CreateLightColorKeyframe(2, 0.2d, 0.3d, 1d);
        colorKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        light.ColorKeyframes = [colorKeyframe];
        scene.Lights.Add(light);
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_key_light.color = (1.0, 0.95, 0.85)"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("light_node_key_light.color = (0.2, 0.3, 1.0)"));
            Assert.That(script, Does.Contain("light_node_key_light.keyframe_insert(data_path='color', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(light_node_key_light, 'color', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsLightRangeKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateLight();
        var keyframe = DccRenderTestData.CreateLightRangeKeyframe(2, 10d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        light.RangeKeyframes = [keyframe];
        scene.Lights.Add(light);
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_key_light.cutoff_distance = 25.0"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("light_node_key_light.cutoff_distance = 10.0"));
            Assert.That(script, Does.Contain("light_node_key_light.keyframe_insert(data_path='cutoff_distance', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(light_node_key_light, 'cutoff_distance', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsSpotLightAngleKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateSpotLight();
        var keyframe = DccRenderTestData.CreateSpotLightAngleKeyframe(2, 20d);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        light.SpotAngleKeyframes = [keyframe];
        scene.Lights.Add(light);
        scene.Nodes.Add(DccRenderTestData.CreateSpotLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_spot_light.spot_size = math.radians(35.0)"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("light_node_spot_light.spot_size = math.radians(20.0)"));
            Assert.That(script, Does.Contain("light_node_spot_light.keyframe_insert(data_path='spot_size', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(light_node_spot_light, 'spot_size', 2, 'LINEAR')"));
        });
    }

    [Test]
    public void CreateEmitsNodeVisibilityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var keyframe = DccRenderTestData.CreateVisibilityKeyframe(2, false, false);
        keyframe.InterpolationMode = DccKeyframeInterpolationMode.Constant;
        scene.Nodes[0].VisibilityKeyframes =
        [
            keyframe
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("object_node_cube.hide_viewport = False"));
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("object_node_cube.hide_viewport = True"));
            Assert.That(script, Does.Contain("object_node_cube.hide_render = True"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='hide_viewport', frame=2)"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='hide_render', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(object_node_cube, 'hide_viewport', 2, 'CONSTANT')"));
        });
    }

    [Test]
    public void CreateEmitsCameraVisibilityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Nodes[^1].VisibilityKeyframes =
        [
            DccRenderTestData.CreateVisibilityKeyframe(2, false, false)
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("object_node_camera_main.hide_viewport = False"));
            Assert.That(script, Does.Contain("object_node_camera_main.hide_viewport = True"));
            Assert.That(script, Does.Contain("object_node_camera_main.hide_render = True"));
            Assert.That(script, Does.Contain("object_node_camera_main.keyframe_insert(data_path='hide_viewport', frame=2)"));
            Assert.That(script, Does.Contain("object_node_camera_main.keyframe_insert(data_path='hide_render', frame=2)"));
        });
    }

    [Test]
    public void CreateEmitsLightVisibilityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        scene.Nodes[^1].VisibilityKeyframes =
        [
            DccRenderTestData.CreateVisibilityKeyframe(2, false, false)
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("object_node_key_light.hide_viewport = False"));
            Assert.That(script, Does.Contain("object_node_key_light.hide_viewport = True"));
            Assert.That(script, Does.Contain("object_node_key_light.hide_render = True"));
            Assert.That(script, Does.Contain("object_node_key_light.keyframe_insert(data_path='hide_viewport', frame=2)"));
            Assert.That(script, Does.Contain("object_node_key_light.keyframe_insert(data_path='hide_render', frame=2)"));
        });
    }

    [Test]
    public void CreateEmitsCombinedTransformAndVisibilityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].TransformKeyframes =
        [
            DccRenderTestData.CreateTransformKeyframe(2, 3d, 4d, 5d)
        ];
        scene.Nodes[0].VisibilityKeyframes =
        [
            DccRenderTestData.CreateVisibilityKeyframe(2, false, false)
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("scene.frame_set(2)"));
            Assert.That(script, Does.Contain("set_transform(object_node_cube, (3.0, 4.0, 5.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0))"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='location', frame=2)"));
            Assert.That(script, Does.Contain("object_node_cube.hide_viewport = True"));
            Assert.That(script, Does.Contain("object_node_cube.hide_render = True"));
            Assert.That(script, Does.Contain("object_node_cube.keyframe_insert(data_path='hide_viewport', frame=2)"));
        });
    }

    [Test]
    public void CreateEmitsCameraCombinedTransformAndVisibilityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        var transformKeyframe = DccRenderTestData.CreateTransformKeyframe(2, 6d, -4d, 3d);
        transformKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var visibilityKeyframe = DccRenderTestData.CreateVisibilityKeyframe(2, false, false);
        visibilityKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Constant;
        scene.Nodes[^1].TransformKeyframes =
        [
            transformKeyframe
        ];
        scene.Nodes[^1].VisibilityKeyframes =
        [
            visibilityKeyframe
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("set_transform(object_node_camera_main, (6.0, -4.0, 3.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0))"));
            Assert.That(script, Does.Contain("object_node_camera_main.keyframe_insert(data_path='location', frame=2)"));
            Assert.That(script, Does.Contain("object_node_camera_main.hide_viewport = True"));
            Assert.That(script, Does.Contain("object_node_camera_main.hide_render = True"));
            Assert.That(script, Does.Contain("object_node_camera_main.keyframe_insert(data_path='hide_viewport', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(object_node_camera_main, 'location', 2, 'LINEAR')"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(object_node_camera_main, 'hide_viewport', 2, 'CONSTANT')"));
        });
    }

    [Test]
    public void CreateEmitsLightCombinedTransformAndVisibilityKeyframesWhenPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var transformKeyframe = DccRenderTestData.CreateTransformKeyframe(2, 5d, -3d, 7d);
        transformKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var visibilityKeyframe = DccRenderTestData.CreateVisibilityKeyframe(2, false, false);
        visibilityKeyframe.InterpolationMode = DccKeyframeInterpolationMode.Constant;
        scene.Nodes[^1].TransformKeyframes =
        [
            transformKeyframe
        ];
        scene.Nodes[^1].VisibilityKeyframes =
        [
            visibilityKeyframe
        ];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("set_transform(object_node_key_light, (5.0, -3.0, 7.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0))"));
            Assert.That(script, Does.Contain("object_node_key_light.keyframe_insert(data_path='location', frame=2)"));
            Assert.That(script, Does.Contain("object_node_key_light.hide_viewport = True"));
            Assert.That(script, Does.Contain("object_node_key_light.hide_render = True"));
            Assert.That(script, Does.Contain("object_node_key_light.keyframe_insert(data_path='hide_viewport', frame=2)"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(object_node_key_light, 'location', 2, 'LINEAR')"));
            Assert.That(script, Does.Contain("set_keyframe_interpolation(object_node_key_light, 'hide_viewport', 2, 'CONSTANT')"));
        });
    }


    #endregion
}
