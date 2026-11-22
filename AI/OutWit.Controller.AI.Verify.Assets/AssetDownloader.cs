using System.Security.Cryptography;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Fetches a pinned download into the cache and verifies it: a cached file that matches its
/// pin is reused, anything that does not match is deleted and refetched once, and a fetch
/// that still does not match is an error. Nothing unverified ever reaches a layer.
/// A local folder (<see cref="LocalDir"/>) may stand in for the network: a file of the pin's
/// name there that matches the pin is taken instead of the download (a release asset before
/// its release exists).
/// </summary>
internal static class AssetDownloader
{
    #region Constants

    /// <summary>The environment variable that names the local folder when --local does not.</summary>
    public const string LOCAL_DIR_ENV = "VERIFY_ASSETS_LOCAL";

    #endregion

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

        var local = LocalDir == null ? null : Path.Combine(LocalDir, pin.FileName);
        if (local != null && File.Exists(local))
        {
            var hash = Sha256Hex(local);
            if (hash != pin.Sha256)
                throw new InvalidOperationException($"{local} hashes to {hash}, pinned {pin.Sha256}");

            File.Copy(local, path, overwrite: true);
            Console.WriteLine($"  {pin.FileName}: taken from {LocalDir}, hash ok");
            return path;
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

    #endregion

    #region Tools

    /// <summary>Lower-case hex SHA-256 of a file.</summary>
    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    #endregion

    #region Properties

    /// <summary>A folder whose files stand in for downloads of the same name, or null.</summary>
    public static string? LocalDir { get; set; }

    #endregion
}
