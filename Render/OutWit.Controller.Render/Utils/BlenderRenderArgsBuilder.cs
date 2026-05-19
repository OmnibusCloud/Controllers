using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Pure static helpers that build the Blender CLI Python configuration arguments
/// used by <see cref="BlenderRunner"/>. Stateless — every method produces a
/// snippet from its inputs only. Extracted from BlenderRunner to keep the
/// orchestrator under the 600-line readability ceiling.
/// </summary>
internal static class BlenderRenderArgsBuilder
{
    #region Constants

    /// <summary>
    /// GPU backends Blender's Cycles compute-device picker can try in order.
    /// Probed at script time; first one with non-CPU devices wins.
    /// </summary>
    private static readonly string[] GPU_BACKEND_CANDIDATES = ["OPTIX", "CUDA", "HIP", "METAL"];

    #endregion

    #region Public

    public static IReadOnlyList<string> BuildDeviceConfigurationPython(RenderEngine engine, bool forceCpuFallback)
    {
        if (engine != RenderEngine.Cycles)
        {
            return
            [
                $"scene.render.engine = '{GetBlenderEngineArgument(engine)}'",
                "print('OUTWIT_RENDER_AVAILABLE=')",
                $"print('OUTWIT_RENDER_BACKEND={GetBlenderEngineArgument(engine)}')",
                $"print('OUTWIT_RENDER_MESSAGE=Using {GetRenderEngineDisplayName(engine)} render path')"
            ];
        }

        if (forceCpuFallback)
        {
            return
            [
                "scene.render.engine = 'CYCLES'",
                "scene.cycles.device = 'CPU'",
                "print('OUTWIT_RENDER_AVAILABLE=')",
                "print('OUTWIT_RENDER_BACKEND=CPU')",
                "print('OUTWIT_RENDER_MESSAGE=Forced CPU fallback after GPU render failure')"
            ];
        }

        var candidateLiteral = "[" + string.Join(", ", GPU_BACKEND_CANDIDATES.Select(ToPythonStringLiteral)) + "]";

        return
        [
            "scene.render.engine = 'CYCLES'",
            "scene.cycles.device = 'CPU'",
            "prefs = bpy.context.preferences.addons['cycles'].preferences if 'cycles' in bpy.context.preferences.addons else None",
            "selected_backend = 'CPU'",
            "def _outwit_refresh_devices():",
            "    if prefs is None:",
            "        return []",
            "    refresh = getattr(prefs, 'refresh_devices', None)",
            "    if callable(refresh):",
            "        refresh()",
            "    else:",
            "        get_devices = getattr(prefs, 'get_devices', None)",
            "        if callable(get_devices):",
            "            get_devices()",
            "    return list(getattr(prefs, 'devices', []))",
            "def _outwit_try_backend(backend):",
            "    if prefs is None:",
            "        return False",
            "    try:",
            "        prefs.compute_device_type = backend",
            "        devices = _outwit_refresh_devices()",
            "        gpu_devices = [device for device in devices if getattr(device, 'type', 'CPU') != 'CPU']",
            "        if len(gpu_devices) == 0:",
            "            return False",
            "        for device in devices:",
            "            device.use = getattr(device, 'type', 'CPU') != 'CPU'",
            "        scene.cycles.device = 'GPU'",
            "        return True",
            "    except Exception:",
            "        return False",
            "available_backends = []",
            $"for backend in {candidateLiteral}:",
            "    if prefs is None:",
            "        break",
            "    try:",
            "        prefs.compute_device_type = backend",
            "        devices = _outwit_refresh_devices()",
            "        gpu_devices = [device for device in devices if getattr(device, 'type', 'CPU') != 'CPU']",
            "        if len(gpu_devices) > 0:",
            "            available_backends.append(backend)",
            "    except Exception:",
            "        pass",
            $"for backend in {candidateLiteral}:",
            "    if _outwit_try_backend(backend):",
            "        selected_backend = backend",
            "        break",
            "selection_message = ('Auto-selected ' + selected_backend) if selected_backend != 'CPU' else ('No GPU backend available; falling back to CPU' if len(available_backends) == 0 else 'GPU backend probe succeeded but Blender still fell back to CPU')",
            "print('OUTWIT_RENDER_AVAILABLE=' + ','.join(available_backends))",
            "print('OUTWIT_RENDER_BACKEND=' + str(selected_backend))",
            "print('OUTWIT_RENDER_MESSAGE=' + selection_message)"
        ];
    }

    public static IReadOnlyList<string> BuildEngineConfigurationPython(RenderOptionsData options)
    {
        var pythonLines = new List<string>();

        if (options.Samples > 0)
        {
            switch (options.Engine)
            {
                case RenderEngine.Cycles:
                    pythonLines.Add($"scene.cycles.samples = {options.Samples}");
                    break;
                case RenderEngine.Eevee:
                case RenderEngine.GreasePencil:
                    pythonLines.Add("eevee = getattr(scene, 'eevee', None)");
                    pythonLines.Add("if eevee is not None:");
                    pythonLines.Add($"    setattr(eevee, 'taa_render_samples', {options.Samples}) if hasattr(eevee, 'taa_render_samples') else None");
                    pythonLines.Add($"    setattr(eevee, 'taa_samples', {options.Samples}) if hasattr(eevee, 'taa_samples') else None");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Engine), options.Engine, null);
            }
        }

        if (options.Denoise)
        {
            switch (options.Engine)
            {
                case RenderEngine.Cycles:
                    pythonLines.Add("scene.cycles.use_denoising = True");
                    break;
                case RenderEngine.Eevee:
                case RenderEngine.GreasePencil:
                    pythonLines.Add("eevee = getattr(scene, 'eevee', None)");
                    pythonLines.Add("if eevee is not None and hasattr(eevee, 'use_taa_reprojection'):");
                    pythonLines.Add("    eevee.use_taa_reprojection = True");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Engine), options.Engine, null);
            }
        }

        return pythonLines;
    }

    public static IReadOnlyList<string> BuildImageOutputConfigurationPython(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => ["scene.render.image_settings.color_mode = 'RGB'"],
            RenderFormat.JPEG => ["scene.render.image_settings.color_mode = 'RGB'"],
            _ => []
        };
    }

    public static IReadOnlyList<string> BuildViewLayerRecoveryPython()
    {
        return
        [
            "view_layers = list(scene.view_layers)",
            "if len(view_layers) > 0 and not any(bool(getattr(layer, 'use', True)) for layer in view_layers):",
            "    for layer in view_layers:",
            "        layer.use = True",
            "    print('OUTWIT_RENDER_RECOVERY=All view layers were disabled; temporarily enabled them for this render')"
        ];
    }

    public static string GetBlenderEngineArgument(RenderEngine engine)
    {
        return engine switch
        {
            RenderEngine.Cycles => "CYCLES",
            RenderEngine.Eevee => "BLENDER_EEVEE_NEXT",
            RenderEngine.GreasePencil => "BLENDER_EEVEE_NEXT",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };
    }

    public static string GetRenderEngineDisplayName(RenderEngine engine)
    {
        return engine switch
        {
            RenderEngine.Cycles => "Cycles",
            RenderEngine.Eevee => "Eevee/Eevee Next",
            RenderEngine.GreasePencil => "Grease Pencil",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };
    }

    public static string BuildPythonExecArgument(IReadOnlyList<string> pythonLines)
    {
        var pythonScript = string.Join("\n", pythonLines);
        return $"--python-expr \"exec({ToPythonStringLiteral(pythonScript)})\"";
    }

    public static string ToPythonStringLiteral(string value)
    {
        return $"'{value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", "\\n")}'";
    }

    public static string FormatToBlenderArg(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => "PNG",
            RenderFormat.EXR => "OPEN_EXR",
            RenderFormat.JPEG => "JPEG",
            _ => "PNG"
        };
    }

    public static string FormatToExtension(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => ".png",
            RenderFormat.EXR => ".exr",
            RenderFormat.JPEG => ".jpg",
            _ => ".png"
        };
    }

    #endregion
}
