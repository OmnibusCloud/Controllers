using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Controller.Render.Dcc.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Tests.Services;

/// <summary>
/// Static-emission tests for DccBlenderSceneScriptGenerator — scene bootstrap,
/// camera/light/material/texture configuration. Excludes keyframe/animation
/// emission, which lives in <see cref="DccBlenderSceneScriptGeneratorAnimationTests"/>.
/// </summary>
[TestFixture]
public sealed class DccBlenderSceneScriptGeneratorTests
{
    #region Tests

    [Test]
    public void CreateContainsSceneBootstrapAndMeshCreationTest()
    {
        var buildInput = DccSceneBuildInputFactory.Create(DccRenderTestData.CreateValidScene());

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("bpy.ops.wm.read_factory_settings(use_empty=True)"));
            Assert.That(script, Does.Contain("scene.name = 'TestScene'"));
            Assert.That(script, Does.Contain("mesh_node_cube = bpy.data.meshes.new('CubeMesh')"));
            Assert.That(script, Does.Contain("object_node_cube = bpy.data.objects.new('Cube', mesh_node_cube)"));
            Assert.That(script, Does.Contain("mesh_node_cube.materials.append(materials_by_id['material:cube'])"));
        });
    }

    [Test]
    public void CreatePrefersAttachmentRelativePathForImageTexturesTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.That(script, Does.Contain("bpy.data.images.load('textures/albedo.png', check_existing=True)"));
    }

    [Test]
    public void CreateEmitsCameraConfigurationWhenCameraNodeIsPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("import mathutils"));
            Assert.That(script, Does.Contain("camera_node_camera_main = bpy.data.cameras.new('MainCamera')"));
            Assert.That(script, Does.Contain("CAMERA_LIGHT_LOCAL_AXIS_CORRECTION = mathutils.Quaternion((1.0, 0.0, 0.0), math.radians(-90.0))"));
            Assert.That(script, Does.Contain("set_transform_with_local_axis_correction(object_node_camera_main"));
            Assert.That(script, Does.Contain("set_camera_vertical_fov(camera_node_camera_main, 45.0)"));
            Assert.That(script, Does.Contain("scene.camera = objects_by_node_id['node:camera-main']"));
        });
    }

    [Test]
    public void CreateEmitsLightConfigurationWhenLightNodeIsPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_key_light = bpy.data.lights.new(name='KeyLight', type='POINT')"));
            Assert.That(script, Does.Contain("light_node_key_light.energy = 1200.0"));
            Assert.That(script, Does.Contain("light_node_key_light.cutoff_distance = 25.0"));
            Assert.That(script, Does.Contain("object_node_key_light = bpy.data.objects.new('KeyLightNode', light_node_key_light)"));
            Assert.That(script, Does.Contain("set_transform_with_local_axis_correction(object_node_key_light"));
        });
    }

    [Test]
    public void CreateEmitsDepthOfFieldWhenCameraEnablesItTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        var camera = scene.Cameras.Last();
        camera.EnableDepthOfField = true;
        camera.FocusDistance = 7.5d;
        camera.FStop = 1.8d;
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain(".dof.use_dof = True"));
            Assert.That(script, Does.Contain(".dof.focus_distance = 7.5"));
            Assert.That(script, Does.Contain(".dof.aperture_fstop = 1.8"));
        });
    }

    [Test]
    public void CreateEmitsAreaLightWithShapeAndShadowToggleTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateLight();
        light.Kind = DccLightKind.Area;
        light.AreaWidth = 2d;
        light.AreaHeight = 3d;
        light.CastShadows = false;
        scene.Lights.Add(light);
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("type='AREA'"));
            Assert.That(script, Does.Contain(".shape = 'RECTANGLE'"));
            Assert.That(script, Does.Contain(".size = 2.0"));
            Assert.That(script, Does.Contain(".size_y = 3.0"));
            Assert.That(script, Does.Contain(".use_shadow = False"));
        });
    }

    [Test]
    public void CreateEmitsColorManagementWhenSpecifiedTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.RenderSettings.ViewTransform = "Standard";
        scene.RenderSettings.Exposure = 1.5d;
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("scene.view_settings.view_transform = 'Standard'"));
            Assert.That(script, Does.Contain("scene.view_settings.exposure = 1.5"));
        });
    }

    [Test]
    public void CreateEmitsSecondUvLayerWhenMeshHasUv1Test()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var mesh = scene.Meshes[0];
        mesh.Uv1 = [.. mesh.Uv0.Select(uv => new DccVector2Data { X = uv.X, Y = uv.Y })];
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("uv_layers.new(name='UVMap')"));
            Assert.That(script, Does.Contain("uv_layers.new(name='UVMap.001')"));
        });
    }

    [Test]
    public void CreateAppliesSmoothCustomNormalsWhenMeshHasNormalsTest()
    {
        var buildInput = DccSceneBuildInputFactory.Create(DccRenderTestData.CreateValidScene());

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("polygon.use_smooth = True"));
            Assert.That(script, Does.Contain(".normals_split_custom_set_from_vertices("));
        });
    }

    [Test]
    public void CreateEmitsWorldBackgroundWhenSceneHasNonBlackWorldTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.World = new DccWorldData
        {
            BackgroundColor = new DccColorData { R = 0.2d, G = 0.3d, B = 0.4d, A = 1d },
            Strength = 1.5d
        };
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("scene.world = scene_world"));
            Assert.That(script, Does.Contain("scene_world_background.inputs['Color'].default_value = (0.2, 0.3, 0.4, 1.0)"));
            Assert.That(script, Does.Contain("scene_world_background.inputs['Strength'].default_value = 1.5"));
        });
    }

    [Test]
    public void CreateEmitsEnvironmentWorldWhenSceneHasEnvironmentImageTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(new DccImageAssetData
        {
            Id = "image:env",
            Name = "Env",
            SourcePath = "C:/hdri/studio.hdr",
            RelativePath = "textures/studio.hdr"
        });
        scene.World = new DccWorldData
        {
            EnvironmentImageId = "image:env",
            Strength = 2.5d,
            EnvironmentRotationDegrees = 90d
        };
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("scene.world = scene_world"));
            Assert.That(script, Does.Contain("world_environment = world_node_tree.nodes.new('ShaderNodeTexEnvironment')"));
            Assert.That(script, Does.Contain("world_environment.image = bpy.data.images.load("));
            Assert.That(script, Does.Contain("world_node_tree.links.new(world_environment.outputs['Color'], world_background.inputs['Color'])"));
            Assert.That(script, Does.Contain("world_background.inputs['Strength'].default_value = 2.5"));
            // Rotation requested -> a mapping node is emitted.
            Assert.That(script, Does.Contain("world_mapping"));
            // The constant-colour background path must not run when an environment image drives the world.
            Assert.That(script, Does.Not.Contain("scene_world_background.inputs['Color']"));
        });
    }

    [Test]
    public void CreateDoesNotEmitWorldWhenSceneHasNoWorldTest()
    {
        var buildInput = DccSceneBuildInputFactory.Create(DccRenderTestData.CreateValidScene());

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("bpy.ops.wm.read_factory_settings(use_empty=True)"));
            Assert.That(script, Does.Not.Contain("scene.world = scene_world"));
        });
    }

    [Test]
    public void CreateDoesNotEmitWorldForBlackWorldTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.World = new DccWorldData
        {
            BackgroundColor = new DccColorData { R = 0d, G = 0d, B = 0d, A = 1d },
            Strength = 1d
        };
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.That(script, Does.Not.Contain("scene.world = scene_world"));
    }

    [Test]
    public void CreateEmitsTransmissionAndIorForGlassMaterialTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].Transmission = 1d;
        scene.Materials[0].Ior = 1.5d;
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("inputs['Transmission Weight'].default_value = 1.0"));
            Assert.That(script, Does.Contain("inputs['IOR'].default_value = 1.5"));
        });
    }

    [Test]
    public void CreateDoesNotEmitTransmissionForOpaqueMaterialTest()
    {
        var buildInput = DccSceneBuildInputFactory.Create(DccRenderTestData.CreateValidScene());

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.That(script, Does.Not.Contain("Transmission Weight"));
    }

    [Test]
    public void CreateEmitsEmissionForSelfIlluminatingMaterialTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].EmissionColor = new DccColorData { R = 1d, G = 0.5d, B = 0d, A = 1d };
        scene.Materials[0].EmissionStrength = 5d;
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("inputs['Emission Color'].default_value = (1.0, 0.5, 0.0, 1.0)"));
            Assert.That(script, Does.Contain("inputs['Emission Strength'].default_value = 5.0"));
        });
    }

    [Test]
    public void CreateEmitsSunLightConfigurationWhenSunLightNodeIsPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSunLight());
        scene.Nodes.Add(DccRenderTestData.CreateSunLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_sun_light = bpy.data.lights.new(name='SunLight', type='SUN')"));
            Assert.That(script, Does.Contain("light_node_sun_light.energy = 3.0"));
            Assert.That(script, Does.Contain("object_node_sun_light = bpy.data.objects.new('SunLightNode', light_node_sun_light)"));
            Assert.That(script, Does.Contain("set_transform_with_local_axis_correction(object_node_sun_light"));
        });
    }

    [Test]
    public void CreateEmitsSpotLightConfigurationWhenSpotLightNodeIsPresentTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Lights.Add(DccRenderTestData.CreateSpotLight());
        scene.Nodes.Add(DccRenderTestData.CreateSpotLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_spot_light = bpy.data.lights.new(name='SpotLight', type='SPOT')"));
            Assert.That(script, Does.Contain("light_node_spot_light.cutoff_distance = 20.0"));
            Assert.That(script, Does.Contain("light_node_spot_light.spot_size = math.radians(35.0)"));
            Assert.That(script, Does.Contain("object_node_spot_light = bpy.data.objects.new('SpotLightNode', light_node_spot_light)"));
            Assert.That(script, Does.Contain("set_transform_with_local_axis_correction(object_node_spot_light"));
        });
    }

    [Test]
    public void CreateDoesNotForceCustomDistanceForSentinelSizedLightRangeTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var light = DccRenderTestData.CreateLight();
        light.Range = 0.01d;
        scene.Lights.Add(light);
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("light_node_key_light.energy = 1200.0"));
            Assert.That(script, Does.Not.Contain("light_node_key_light.use_custom_distance = True"));
            Assert.That(script, Does.Not.Contain("light_node_key_light.cutoff_distance = 0.01"));
        });
    }

    [Test]
    public void CreateEmitsMetallicAndRoughnessTextureConfigurationTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateMetallicImageAsset());
        scene.ImageAssets.Add(DccRenderTestData.CreateRoughnessImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Metallic,
            ImageAssetId = "image:metallic"
        });
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Roughness,
            ImageAssetId = "image:roughness"
        });
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("texture_material_material_cube_metallic = material_material_cube_nodes.new('ShaderNodeTexImage')"));
            Assert.That(script, Does.Contain("texture_material_material_cube_metallic.image.colorspace_settings.name = 'Non-Color'"));
            Assert.That(script, Does.Contain("metallic_value_material_material_cube = material_material_cube_nodes.new('ShaderNodeValue')"));
            Assert.That(script, Does.Contain("metallic_multiply_material_material_cube = material_material_cube_nodes.new('ShaderNodeMath')"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texture_material_material_cube_metallic.outputs['Color'], metallic_multiply_material_material_cube.inputs[0])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(metallic_multiply_material_material_cube.outputs['Value'], material_material_cube_bsdf.inputs['Metallic'])"));
            Assert.That(script, Does.Contain("texture_material_material_cube_roughness.image.colorspace_settings.name = 'Non-Color'"));
            Assert.That(script, Does.Contain("roughness_value_material_material_cube = material_material_cube_nodes.new('ShaderNodeValue')"));
            Assert.That(script, Does.Contain("roughness_multiply_material_material_cube = material_material_cube_nodes.new('ShaderNodeMath')"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texture_material_material_cube_roughness.outputs['Color'], roughness_multiply_material_material_cube.inputs[0])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(roughness_multiply_material_material_cube.outputs['Value'], material_material_cube_bsdf.inputs['Roughness'])"));
        });
    }

    [Test]
    public void CreateEmitsNormalTextureConfigurationTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateNormalImageAsset());
        scene.Materials[0].NormalStrength = 1.75d;
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Normal,
            ImageAssetId = "image:normal"
        });
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("texture_material_material_cube_normal = material_material_cube_nodes.new('ShaderNodeTexImage')"));
            Assert.That(script, Does.Contain("texture_material_material_cube_normal.image.colorspace_settings.name = 'Non-Color'"));
            Assert.That(script, Does.Contain("normal_map_material_material_cube = material_material_cube_nodes.new('ShaderNodeNormalMap')"));
            Assert.That(script, Does.Contain("normal_map_material_material_cube.inputs['Strength'].default_value = 1.75"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texture_material_material_cube_normal.outputs['Color'], normal_map_material_material_cube.inputs['Color'])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(normal_map_material_material_cube.outputs['Normal'], material_material_cube_bsdf.inputs['Normal'])"));
        });
    }

    [Test]
    public void CreateEmitsOpacityTextureConfigurationTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateOpacityImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Opacity,
            ImageAssetId = "image:opacity"
        });
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("material_material_cube.blend_method = 'BLEND'"));
            Assert.That(script, Does.Contain("texture_material_material_cube_opacity = material_material_cube_nodes.new('ShaderNodeTexImage')"));
            Assert.That(script, Does.Contain("opacity_multiply_material_material_cube = material_material_cube_nodes.new('ShaderNodeMath')"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texture_material_material_cube_opacity.outputs['Alpha'], opacity_multiply_material_material_cube.inputs[0])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(opacity_multiply_material_material_cube.outputs['Value'], material_material_cube_bsdf.inputs['Alpha'])"));
        });
    }

    [Test]
    public void CreateEmitsOpacityClipConfigurationTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateOpacityImageAsset());
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;
        scene.Materials[0].AlphaClipThreshold = 0.33d;
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Opacity,
            ImageAssetId = "image:opacity"
        });
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("material_material_cube.blend_method = 'CLIP'"));
            Assert.That(script, Does.Contain("material_material_cube.shadow_method = 'CLIP'"));
            Assert.That(script, Does.Contain("material_material_cube.alpha_threshold = 0.33"));
        });
    }

    [Test]
    public void CreateEmitsOpacityHashedConfigurationTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateOpacityImageAsset());
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Hashed;
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Opacity,
            ImageAssetId = "image:opacity"
        });
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("material_material_cube.blend_method = 'HASHED'"));
            Assert.That(script, Does.Contain("material_material_cube.shadow_method = 'HASHED'"));
        });
    }

    [Test]
    public void CreateEmitsCombinedBaseColorTextureAndScalarBaseColorControlTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("base_color_value_material_material_cube.outputs[0].default_value = (0.8, 0.7, 0.6, 1.0)"));
            Assert.That(script, Does.Contain("base_color_multiply_material_material_cube = material_material_cube_nodes.new('ShaderNodeMixRGB')"));
            Assert.That(script, Does.Contain("base_color_multiply_material_material_cube.blend_type = 'MULTIPLY'"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texture_material_material_cube_base_color.outputs['Color'], base_color_multiply_material_material_cube.inputs['Color1'])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(base_color_multiply_material_material_cube.outputs['Color'], material_material_cube_bsdf.inputs['Base Color'])"));
        });
    }

    [Test]
    public void CreateEmitsCombinedOpacityTextureAndScalarOpacityControlTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.ImageAssets.Add(DccRenderTestData.CreateOpacityImageAsset());
        scene.Materials[0].Opacity = 0.5d;
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Opacity,
            ImageAssetId = "image:opacity"
        });
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("opacity_value_material_material_cube.outputs[0].default_value = 0.5"));
            Assert.That(script, Does.Contain("opacity_multiply_material_material_cube = material_material_cube_nodes.new('ShaderNodeMath')"));
            Assert.That(script, Does.Contain("opacity_multiply_material_material_cube.operation = 'MULTIPLY'"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texture_material_material_cube_opacity.outputs['Alpha'], opacity_multiply_material_material_cube.inputs[0])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(opacity_multiply_material_material_cube.outputs['Value'], material_material_cube_bsdf.inputs['Alpha'])"));
        });
    }

    [Test]
    public void CreateEmitsUvTransformConfigurationForTextureSlotTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots[0].UvScaleX = 2d;
        scene.Materials[0].TextureSlots[0].UvScaleY = 0.5d;
        scene.Materials[0].TextureSlots[0].UvOffsetX = 0.25d;
        scene.Materials[0].TextureSlots[0].UvOffsetY = -0.1d;
        scene.Materials[0].TextureSlots[0].UvRotationDegrees = 45d;
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("texcoord_material_material_cube_base_color = material_material_cube_nodes.new('ShaderNodeTexCoord')"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color = material_material_cube_nodes.new('ShaderNodeMapping')"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color.inputs['Location'].default_value[0] = 0.25"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color.inputs['Location'].default_value[1] = -0.1"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color.inputs['Rotation'].default_value[2] = math.radians(45.0)"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color.inputs['Scale'].default_value[0] = 2.0"));
            Assert.That(script, Does.Contain("mapping_material_material_cube_base_color.inputs['Scale'].default_value[1] = 0.5"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(texcoord_material_material_cube_base_color.outputs['UV'], mapping_material_material_cube_base_color.inputs['Vector'])"));
            Assert.That(script, Does.Contain("material_material_cube_links.new(mapping_material_material_cube_base_color.outputs['Vector'], texture_material_material_cube_base_color.inputs['Vector'])"));
        });
    }

    [Test]
    public void CreateEmitsMultiMaterialMeshConfigurationTest()
    {
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials.Add(DccRenderTestData.CreateSecondaryMaterial());
        DccRenderTestData.ApplyTwoTriangleQuadMesh(scene);
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var script = DccBlenderSceneScriptGenerator.Create(buildInput);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("mesh_node_cube.materials.append(materials_by_id['material:cube'])"));
            Assert.That(script, Does.Contain("mesh_node_cube.materials.append(materials_by_id['material:secondary'])"));
            Assert.That(script, Does.Contain("mesh_node_cube.polygons[0].material_index = 0"));
            Assert.That(script, Does.Contain("mesh_node_cube.polygons[1].material_index = 1"));
        });
    }


    #endregion
}
