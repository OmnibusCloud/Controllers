using System.Security.Cryptography;

namespace OutWit.Controller.Render.Tests.Utils;

internal static class RenderGoldenFileAssert
{
    #region Constants

    private const string UPDATE_ENVIRONMENT_VARIABLE = "WIT_RENDER_UPDATE_GOLDENS";

    #endregion

    #region Functions

    public static void AssertMatchesOrUpdate(string actualPath, string expectedPath, string assetName)
    {
        if (!File.Exists(actualPath))
            Assert.Fail($"Actual render output for '{assetName}' was not found at '{actualPath}'.");

        if (!File.Exists(expectedPath))
        {
            if (ShouldUpdateGoldens())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
                File.Copy(actualPath, expectedPath, overwrite: true);
                Assert.Inconclusive($"Golden file for '{assetName}' was created at '{expectedPath}'. Re-run the test to validate against it.");
            }

            Assert.Ignore($"Golden file for '{assetName}' is missing at '{expectedPath}'. Set {UPDATE_ENVIRONMENT_VARIABLE}=1 to generate/update it.");
        }

        var actualHash = ComputeSha256(actualPath);
        var expectedHash = ComputeSha256(expectedPath);

        Assert.That(actualHash, Is.EqualTo(expectedHash),
            $"Rendered output for '{assetName}' does not match the golden file '{expectedPath}'.");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool ShouldUpdateGoldens()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(UPDATE_ENVIRONMENT_VARIABLE),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
