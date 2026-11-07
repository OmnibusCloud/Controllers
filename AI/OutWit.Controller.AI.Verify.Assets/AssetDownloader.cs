using System.Security.Cryptography;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Fetches a pinned download into the cache and verifies it: a cached file that matches its
/// pin is reused, anything that does not match is deleted and refetched once, and a fetch
/// that still does not match is an error. Nothing unverified ever reaches a layer.
/// </summary>
internal static class AssetDownloader
{
    #region Fields

    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromMinutes(10) };

    #endregion

    #region Functions

    /// <summary>
    /// Returns the verified local path of a pin, downloading it when the cache lacks it.
    /// </summary>
    /// <param name="pin">The pin.</param>
    /// <param name="cacheDir">The cache folder.</param>
    /// <param name="offline">True to refuse downloads (the cache must already hold the file).</param>
    /// <returns>The verified file path.</returns>
    /// <exception cref="InvalidOperationException">The file is missing offline, or does not match its pin.</exception>
    public static async Task<string> FetchAsync(AssetPin pin, string cacheDir, bool offline)
    {
        Directory.CreateDirectory(cacheDir);
        var path = Path.Combine(cacheDir, pin.FileName);

        if (File.Exists(path))
        {
            if (Sha256Hex(path) == pin.Sha256)
            {
                Console.WriteLine($"  {pin.FileName}: cached, hash ok");
                return path;
            }

            Console.WriteLine($"  {pin.FileName}: cached file fails its pin, refetching");
            File.Delete(path);
        }

        if (offline)
            throw new InvalidOperationException($"{pin.FileName} is not in the cache and the build is offline");

        Console.WriteLine($"  {pin.FileName}: downloading {pin.Url}");
        var temp = path + ".part";
        await using (var response = await s_http.GetStreamAsync(pin.Url))
        await using (var file = File.Create(temp))
        {
            await response.CopyToAsync(file);
        }

        var actual = Sha256Hex(temp);
        if (actual != pin.Sha256)
        {
            File.Delete(temp);
            throw new InvalidOperationException($"{pin.FileName}: downloaded bytes hash to {actual}, pinned {pin.Sha256}");
        }

        File.Move(temp, path);
        Console.WriteLine($"  {pin.FileName}: {new FileInfo(path).Length:N0} bytes, hash ok");
        return path;
    }

    /// <summary>Lower-case hex SHA-256 of a file.</summary>
    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    #endregion
}
