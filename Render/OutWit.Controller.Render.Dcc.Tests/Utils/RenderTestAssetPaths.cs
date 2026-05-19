using System.Runtime.InteropServices;

namespace OutWit.Controller.Render.Dcc.Tests.Utils;

internal static class RenderTestAssetPaths
{
    public static string? FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OutWit.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    public static string? FindControllersPath()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            return null;

        var config = "Debug";
        var path = Path.Combine(solutionRoot, "@Controllers", config);
        return Directory.Exists(path) ? path : null;
    }

    public static string? FindRenderBlenderRoot()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            return null;

        var config = "Debug";
        var candidateRoots = new[]
        {
            Path.Combine(solutionRoot, "@Controllers", config, "render.module", "blender"),
            Path.Combine(solutionRoot, "@Prerequisites", "blender")
        };

        return candidateRoots.FirstOrDefault(me => File.Exists(ResolveBlenderExecutablePath(me)));
    }

    private static string ResolveBlenderExecutablePath(string blenderRoot)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(blenderRoot, "windows-x64", "blender.exe");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(blenderRoot, "macos-arm64", "Blender.app", "Contents", "MacOS", "Blender");

        return Path.Combine(blenderRoot, "linux-x64", "blender");
    }
}
