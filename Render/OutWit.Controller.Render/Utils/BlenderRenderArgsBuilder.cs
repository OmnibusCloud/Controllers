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
            // Blender renamed the EEVEE engine id across versions: 4.2–4.x use 'BLENDER_EEVEE_NEXT',
            // 5.x (the bundled runtime) uses 'BLENDER_EEVEE'. Pick whichever the running Blender
            // actually exposes so Eevee / Grease Pencil work regardless of bundled version.
            var preferred = GetBlenderEngineArgument(engine);
            return
            [
                "_engine_keys = bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items.keys()",
                $"scene.render.engine = '{preferred}' if '{preferred}' in _engine_keys else ('BLENDER_EEVEE' if 'BLENDER_EEVEE' in _engine_keys else '{preferred}')",
                "print('OUTWIT_RENDER_AVAILABLE=')",
                "print('OUTWIT_RENDER_BACKEND=' + scene.render.engine)",
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

    public static IReadOnlyList<string> BuildImageOutputConfigurationPython(RenderOptionsData options)
    {
        var lines = new List<string>();

        // Colour channels. Default reproduces legacy behaviour: force RGB for the 8-bit formats,
        // leave EXR to the scene. RGBA enables alpha output (transparent renders / compositing).
        // Guarded like color_depth: not every mode is valid for every format (JPEG cannot carry
        // alpha) and Blender raises on an unsupported value.
        var colorMode = options.ColorMode switch
        {
            RenderColorMode.RGB => "RGB",
            RenderColorMode.RGBA => "RGBA",
            _ => options.Format is RenderFormat.PNG or RenderFormat.JPEG or RenderFormat.TIFF or RenderFormat.WEBP
                ? "RGB"
                : null
        };
        if (colorMode is not null)
        {
            lines.Add("try:");
            lines.Add($"    scene.render.image_settings.color_mode = '{colorMode}'");
            lines.Add("except (TypeError, ValueError):");
            lines.Add("    pass");
        }

        // Film/world transparency. Default leaves the scene's own setting untouched.
        switch (options.FilmTransparent)
        {
            case RenderFilmTransparency.Transparent:
                lines.Add("scene.render.film_transparent = True");
                break;
            case RenderFilmTransparency.Opaque:
                lines.Add("scene.render.film_transparent = False");
                break;
        }

        // Bit depth. Default leaves the scene/format default untouched. Guarded because not every depth is
        // valid for every format and Blender raises on an unsupported value.
        var colorDepth = options.ColorDepth switch
        {
            RenderColorDepth.Eight => "8",
            RenderColorDepth.Sixteen => "16",
            RenderColorDepth.ThirtyTwo => "32",
            _ => null
        };
        if (colorDepth is not null)
        {
            lines.Add("try:");
            lines.Add($"    scene.render.image_settings.color_depth = '{colorDepth}'");
            lines.Add("except (TypeError, ValueError):");
            lines.Add("    pass");
        }

        return lines;
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
            RenderFormat.TIFF => "TIFF",
            RenderFormat.WEBP => "WEBP",
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
            RenderFormat.TIFF => ".tif",
            RenderFormat.WEBP => ".webp",
            _ => ".png"
        };
    }

    #endregion
}
