using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Models.Build;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccSceneBuildInputFactory
{
    #region Functions

    public static DccSceneBuildInput Create(DccSceneData scene)
    {
        DccSceneContractValidator.Validate(scene);

        var normalizedScene = (DccSceneData)scene.Clone();
        var nodesById = normalizedScene.Nodes.ToDictionary(me => me.Id, StringComparer.Ordinal);
        var meshesById = normalizedScene.Meshes.ToDictionary(me => me.Id, StringComparer.Ordinal);
        var materialsById = normalizedScene.Materials.ToDictionary(me => me.Id, StringComparer.Ordinal);
        var imageAssetsById = normalizedScene.ImageAssets.ToDictionary(me => me.Id, StringComparer.Ordinal);

        ValidateNodeHierarchy(normalizedScene, nodesById);
        ValidateNodeAnimations(normalizedScene);
        ValidateCameraAnimations(normalizedScene);
        ValidateLightAnimations(normalizedScene);
        ValidateMaterialAnimations(normalizedScene);
        ValidateMeshTopology(normalizedScene, materialsById.Count);

        var imageAttachmentsByImageId = BuildImageAttachmentsByImageId(normalizedScene);
        ValidateReferencedImageAttachments(normalizedScene, imageAssetsById, imageAttachmentsByImageId);

        return new DccSceneBuildInput
        {
            Scene = normalizedScene,
            UnitsToMetersScale = 1d / normalizedScene.Units.UnitsPerMeter,
            NodesById = nodesById,
            MeshesById = meshesById,
            MaterialsById = materialsById,
            ImageAssetsById = imageAssetsById,
            ImageAttachmentsByImageId = imageAttachmentsByImageId
        };
    }

    private static Dictionary<string, RenderSceneAttachmentRefData> BuildImageAttachmentsByImageId(DccSceneData scene)
    {
        var attachmentsByImageId = new Dictionary<string, RenderSceneAttachmentRefData>(StringComparer.Ordinal);

        foreach (var imageAsset in scene.ImageAssets)
        {
            var matchingAttachments = scene.AttachedFiles
                .Where(me => me.Kind == "ImageAsset"
                             && ((me.RelativePath == imageAsset.RelativePath && !string.IsNullOrWhiteSpace(imageAsset.RelativePath))
                                 || (me.OriginalPath == imageAsset.SourcePath && !string.IsNullOrWhiteSpace(imageAsset.SourcePath))))
                .ToList();

            if (matchingAttachments.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene image asset '{imageAsset.Id}' has multiple matching attachments.");
            }

            if (matchingAttachments.Count == 1)
                attachmentsByImageId[imageAsset.Id] = matchingAttachments[0];
        }

        return attachmentsByImageId;
    }

    private static void ValidateReferencedImageAttachments(
        DccSceneData scene,
        IReadOnlyDictionary<string, DccImageAssetData> imageAssetsById,
        IReadOnlyDictionary<string, RenderSceneAttachmentRefData> attachmentsByImageId)
    {
        // Fail fast on textures that never made it into AttachedFiles: without a materialized
        // attachment the script generator would fall back to a client-machine path and the build
        // would only fail at image-load time deep inside Blender.
        foreach (var material in scene.Materials)
        {
            foreach (var textureSlot in material.TextureSlots)
            {
                if (attachmentsByImageId.ContainsKey(textureSlot.ImageAssetId))
                    continue;

                var imageAsset = imageAssetsById[textureSlot.ImageAssetId];
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' texture slot '{textureSlot.Slot}' references image asset '{imageAsset.Id}' with no matching attachment for '{DescribeImagePath(imageAsset)}'.");
            }
        }

        if (scene.World != null
            && !string.IsNullOrWhiteSpace(scene.World.EnvironmentImageId)
            && !attachmentsByImageId.ContainsKey(scene.World.EnvironmentImageId))
        {
            var imageAsset = imageAssetsById[scene.World.EnvironmentImageId];
            throw new InvalidOperationException(
                $"Render.BuildBlendFromDccScene world environment image '{imageAsset.Id}' has no matching attachment for '{DescribeImagePath(imageAsset)}'.");
        }
    }

    private static string DescribeImagePath(DccImageAssetData imageAsset)
    {
        return string.IsNullOrWhiteSpace(imageAsset.RelativePath)
            ? imageAsset.SourcePath
            : imageAsset.RelativePath;
    }

    private static void ValidateMeshTopology(DccSceneData scene, int materialCount)
    {
        foreach (var mesh in scene.Meshes)
        {
            var triangleCount = mesh.TriangleIndices.Count / 3;

            if (mesh.Positions.Count == 0)
                throw new InvalidOperationException($"Render.BuildBlendFromDccScene mesh '{mesh.Id}' requires positions.");

            if (mesh.Normals.Count > 0 && mesh.Normals.Count != mesh.Positions.Count)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' normals count must match positions count when normals are provided.");
            }

            if (mesh.Uv0.Count > 0 && mesh.Uv0.Count != mesh.Positions.Count)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' uv count must match positions count when UVs are provided.");
            }

            // Uv1 is gathered through the same per-vertex triangle fancy-index as Uv0; a count that
            // does not match the vertex count would index out of range (numpy IndexError deep inside
            // Blender) or silently drop rows, so reject it up front like Uv0 rather than fail mid-build.
            if (mesh.Uv1.Count > 0 && mesh.Uv1.Count != mesh.Positions.Count)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' second uv count must match positions count when a second UV channel is provided.");
            }

            if (mesh.Colors.Count > 0 && mesh.Colors.Count != mesh.Positions.Count)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' vertex color count must match positions count when colors are provided.");
            }

            if (mesh.TriangleIndices.Count % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' triangle indices count must be divisible by 3.");
            }

            // Each subdivision level quadruples the face count at render time; anything beyond a
            // few levels is an exporter bug, not artist intent.
            if (mesh.SubdivisionLevels is < 0 or > 6)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' requires SubdivisionLevels in the [0, 6] range.");
            }

            if (mesh.MaterialIndices.Count > 0 && mesh.MaterialIndices.Count != triangleCount)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' material indices count must match triangle count when material indices are provided.");
            }

            foreach (var triangleIndex in mesh.TriangleIndices)
            {
                if (triangleIndex < 0 || triangleIndex >= mesh.Positions.Count)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' contains an out-of-range triangle index '{triangleIndex}'.");
                }
            }

            foreach (var materialIndex in mesh.MaterialIndices)
            {
                if (materialIndex < 0 || materialIndex >= materialCount)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' contains an out-of-range material index '{materialIndex}'.");
                }
            }

            // Baked deformation frames are applied as shape keys whose vertex array is the mesh's own
            // vertex count - a frame with a different position count would index out of range (or
            // leave a partial pose) in Blender, so reject the mismatch up front.
            var seenDeformationFrames = new HashSet<int>();
            foreach (var deformationFrame in mesh.DeformationFrames)
            {
                if (deformationFrame.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' contains a negative deformation frame '{deformationFrame.Frame}'.");
                }

                if (deformationFrame.Positions.Count != mesh.Positions.Count)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' deformation frame '{deformationFrame.Frame}' position count must match the mesh vertex count.");
                }

                if (!seenDeformationFrames.Add(deformationFrame.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene mesh '{mesh.Id}' contains duplicate deformation frame '{deformationFrame.Frame}'.");
                }
            }
        }
    }

    private static void ValidateLightAnimations(DccSceneData scene)
    {
        foreach (var light in scene.Lights)
        {
            var seenColorFrames = new HashSet<int>();
            var seenIntensityFrames = new HashSet<int>();
            var seenRangeFrames = new HashSet<int>();
            var seenSpotAngleFrames = new HashSet<int>();

            foreach (var keyframe in light.ColorKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains a negative color keyframe frame '{keyframe.Frame}'.");
                }

                if (Math.Abs(keyframe.Color.A - 1d) > 1e-9)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains unsupported light-color keyframe alpha '{keyframe.Color.A}'.");
                }

                if (!seenColorFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains duplicate color keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in light.IntensityKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains a negative intensity keyframe frame '{keyframe.Frame}'.");
                }

                if (!seenIntensityFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains duplicate intensity keyframe frame '{keyframe.Frame}'.");
                }
            }

            if (light.RangeKeyframes.Count > 0 && light.Kind is not (DccLightKind.Point or DccLightKind.Spot))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' can use range keyframes only when Kind is Point or Spot.");
            }

            foreach (var keyframe in light.RangeKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains a negative range keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value <= 0d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains an out-of-range range keyframe value '{keyframe.Value}'.");
                }

                if (!seenRangeFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains duplicate range keyframe frame '{keyframe.Frame}'.");
                }
            }

            if (light.SpotAngleKeyframes.Count > 0 && light.Kind != DccLightKind.Spot)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene light '{light.Id}' can use spot-angle keyframes only when Kind is Spot.");
            }

            foreach (var keyframe in light.SpotAngleKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains a negative spot-angle keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value <= 0d || keyframe.Value > 180d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains an out-of-range spot-angle keyframe value '{keyframe.Value}'.");
                }

                if (!seenSpotAngleFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene light '{light.Id}' contains duplicate spot-angle keyframe frame '{keyframe.Frame}'.");
                }
            }
        }
    }

    private static void ValidateNodeHierarchy(
        DccSceneData scene,
        IReadOnlyDictionary<string, DccNodeData> nodesById)
    {
        foreach (var node in scene.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ParentId))
                continue;

            if (node.ParentId == node.Id || !nodesById.ContainsKey(node.ParentId))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene node '{node.Id}' references missing parent node '{node.ParentId}'.");
            }
        }
    }

    private static void ValidateNodeAnimations(DccSceneData scene)
    {
        foreach (var node in scene.Nodes)
        {
            var seenFrames = new HashSet<int>();
            var seenVisibilityFrames = new HashSet<int>();

            foreach (var keyframe in node.TransformKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene node '{node.Id}' contains a negative transform keyframe frame '{keyframe.Frame}'.");
                }

                if (!seenFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene node '{node.Id}' contains duplicate transform keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in node.VisibilityKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene node '{node.Id}' contains a negative visibility keyframe frame '{keyframe.Frame}'.");
                }

                if (!seenVisibilityFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene node '{node.Id}' contains duplicate visibility keyframe frame '{keyframe.Frame}'.");
                }
            }
        }
    }

    private static void ValidateMaterialAnimations(DccSceneData scene)
    {
        foreach (var material in scene.Materials)
        {
            var seenBaseColorFrames = new HashSet<int>();
            var seenAlphaClipThresholdFrames = new HashSet<int>();
            var seenOpacityFrames = new HashSet<int>();
            var seenMetallicFrames = new HashSet<int>();
            var seenNormalStrengthFrames = new HashSet<int>();
            var seenRoughnessFrames = new HashSet<int>();

            foreach (var keyframe in material.BaseColorKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains a negative base-color keyframe frame '{keyframe.Frame}'.");
                }

                if (Math.Abs(keyframe.Color.A - 1d) > 1e-9)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains unsupported base-color keyframe alpha '{keyframe.Color.A}'. Use opacity animation for transparency.");
                }

                if (!seenBaseColorFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate base-color keyframe frame '{keyframe.Frame}'.");
                }
            }

            if (material.AlphaClipThresholdKeyframes.Count > 0 && material.AlphaMode != DccMaterialAlphaMode.Clip)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' can use alpha-clip-threshold keyframes only when AlphaMode is Clip.");
            }

            foreach (var keyframe in material.AlphaClipThresholdKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains a negative alpha-clip-threshold keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value < 0d || keyframe.Value > 1d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains an out-of-range alpha-clip-threshold keyframe value '{keyframe.Value}'.");
                }

                if (!seenAlphaClipThresholdFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate alpha-clip-threshold keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in material.OpacityKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains a negative opacity keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value < 0d || keyframe.Value > 1d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains an out-of-range opacity keyframe value '{keyframe.Value}'.");
                }

                if (!seenOpacityFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate opacity keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in material.MetallicKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains a negative metallic keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value < 0d || keyframe.Value > 1d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains an out-of-range metallic keyframe value '{keyframe.Value}'.");
                }

                if (!seenMetallicFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate metallic keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in material.RoughnessKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains a negative roughness keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value < 0d || keyframe.Value > 1d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains an out-of-range roughness keyframe value '{keyframe.Value}'.");
                }

                if (!seenRoughnessFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate roughness keyframe frame '{keyframe.Frame}'.");
                }
            }

            if (material.NormalStrengthKeyframes.Count > 0 && material.TextureSlots.All(me => me.Slot != DccTextureSlotKind.Normal))
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene material '{material.Id}' can use normal-strength keyframes only when a normal texture slot is present.");
            }

            foreach (var keyframe in material.NormalStrengthKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains a negative normal-strength keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value < 0d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains an out-of-range normal-strength keyframe value '{keyframe.Value}'.");
                }

                if (!seenNormalStrengthFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene material '{material.Id}' contains duplicate normal-strength keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var textureSlot in material.TextureSlots)
            {
                var seenUvTransformFrames = new HashSet<int>();

                foreach (var keyframe in textureSlot.UvTransformKeyframes)
                {
                    if (keyframe.Frame < 0)
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene material '{material.Id}' texture slot '{textureSlot.Slot}' contains a negative UV-transform keyframe frame '{keyframe.Frame}'.");
                    }

                    if (!seenUvTransformFrames.Add(keyframe.Frame))
                    {
                        throw new InvalidOperationException(
                            $"Render.BuildBlendFromDccScene material '{material.Id}' texture slot '{textureSlot.Slot}' contains duplicate UV-transform keyframe frame '{keyframe.Frame}'.");
                    }
                }
            }
        }
    }

    private static void ValidateCameraAnimations(DccSceneData scene)
    {
        foreach (var camera in scene.Cameras)
        {
            var seenFovFrames = new HashSet<int>();
            var seenNearClipFrames = new HashSet<int>();
            var seenFarClipFrames = new HashSet<int>();

            if (camera.VerticalFovKeyframes.Count > 0 && !camera.IsPerspective)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene camera '{camera.Id}' can use vertical-FOV keyframes only when IsPerspective is true.");
            }

            foreach (var keyframe in camera.VerticalFovKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains a negative vertical-FOV keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value <= 0d || keyframe.Value >= 180d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains an out-of-range vertical-FOV keyframe value '{keyframe.Value}'.");
                }

                if (!seenFovFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains duplicate vertical-FOV keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in camera.NearClipKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains a negative near-clip keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value <= 0d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains an out-of-range near-clip keyframe value '{keyframe.Value}'.");
                }

                if (!seenNearClipFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains duplicate near-clip keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var keyframe in camera.FarClipKeyframes)
            {
                if (keyframe.Frame < 0)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains a negative far-clip keyframe frame '{keyframe.Frame}'.");
                }

                if (keyframe.Value <= 0d)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains an out-of-range far-clip keyframe value '{keyframe.Value}'.");
                }

                if (!seenFarClipFrames.Add(keyframe.Frame))
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains duplicate far-clip keyframe frame '{keyframe.Frame}'.");
                }
            }

            foreach (var nearClipKeyframe in camera.NearClipKeyframes)
            {
                var farClipAtFrame = camera.FarClipKeyframes.FirstOrDefault(me => me.Frame == nearClipKeyframe.Frame)?.Value ?? camera.FarClip;
                if (nearClipKeyframe.Value >= farClipAtFrame)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains near/far clip keyframes with invalid ordering at frame '{nearClipKeyframe.Frame}'.");
                }
            }

            foreach (var farClipKeyframe in camera.FarClipKeyframes)
            {
                var nearClipAtFrame = camera.NearClipKeyframes.FirstOrDefault(me => me.Frame == farClipKeyframe.Frame)?.Value ?? camera.NearClip;
                if (nearClipAtFrame >= farClipKeyframe.Value)
                {
                    throw new InvalidOperationException(
                        $"Render.BuildBlendFromDccScene camera '{camera.Id}' contains near/far clip keyframes with invalid ordering at frame '{farClipKeyframe.Frame}'.");
                }
            }
        }
    }

    #endregion
}
