using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// The deterministic identities of the distribution model: the package digest (state + every
/// attachment, order-independent), the output options digest (everything per-output, not the
/// frame selection), and the task identity
/// <c>package digest + dataset identity + view id + timestep index + output options digest</c>
/// (docs 03, section 11). The dataset identity component is reserved and empty in version 1, so
/// the dataset-set scenario later changes no version-1 task identity.
/// </summary>
public static class ParaViewPackageDigest
{
    #region Constants

    private const string FIELD_SEPARATOR = "\n";

    private const string RECORD_SEPARATOR = "|";

    #endregion

    #region Functions

    /// <summary>
    /// Digest of the whole package: the state digest and size, then every attachment's logical
    /// path, digest and size sorted by logical path.
    /// </summary>
    /// <param name="scene">The package reference.</param>
    /// <returns>Lower-case hexadecimal SHA-256.</returns>
    public static string ComputePackageDigest(ParaViewSceneRefData scene)
    {
        var builder = new StringBuilder();
        builder.Append("state").Append(RECORD_SEPARATOR)
            .Append(scene.StateSha256.ToLowerInvariant()).Append(RECORD_SEPARATOR)
            .Append(scene.StateSize.ToString(CultureInfo.InvariantCulture)).Append(FIELD_SEPARATOR);

        foreach (var attachment in scene.Attachments.OrderBy(me => me.LogicalPath, StringComparer.Ordinal))
        {
            builder.Append(attachment.LogicalPath).Append(RECORD_SEPARATOR)
                .Append(attachment.Sha256.ToLowerInvariant()).Append(RECORD_SEPARATOR)
                .Append(attachment.Size.ToString(CultureInfo.InvariantCulture)).Append(FIELD_SEPARATOR);
        }

        return Hash(builder.ToString());
    }

    /// <summary>
    /// Digest of the per-output options: view, size, format, transparency. The frame selection is
    /// deliberately excluded — it determines the task set, not a task's identity.
    /// </summary>
    /// <param name="options">The output options.</param>
    /// <param name="resolvedViewId">The resolved view registration name.</param>
    /// <returns>Lower-case hexadecimal SHA-256.</returns>
    public static string ComputeOptionsDigest(ParaViewOutputOptionsData options, string resolvedViewId)
    {
        var text = string.Join(RECORD_SEPARATOR,
            "options@1",
            resolvedViewId,
            options.Width.ToString(CultureInfo.InvariantCulture),
            options.Height.ToString(CultureInfo.InvariantCulture),
            options.Format.ToString(),
            options.TransparentBackground ? "transparent" : "opaque");

        return Hash(text);
    }

    /// <summary>
    /// The task identity.
    /// </summary>
    /// <param name="packageDigest">Package digest.</param>
    /// <param name="datasetId">Dataset identity component (empty in version 1).</param>
    /// <param name="viewId">Resolved view registration name.</param>
    /// <param name="timestepIndex">Timestep index.</param>
    /// <param name="optionsDigest">Output options digest.</param>
    /// <returns>Lower-case hexadecimal SHA-256.</returns>
    public static string ComputeTaskId(string packageDigest, string datasetId, string viewId, int timestepIndex, string optionsDigest)
    {
        var text = string.Join(RECORD_SEPARATOR,
            "task@1",
            packageDigest,
            datasetId,
            viewId,
            timestepIndex.ToString(CultureInfo.InvariantCulture),
            optionsDigest);

        return Hash(text);
    }

    /// <summary>
    /// Lower-case hexadecimal SHA-256 of a file's content.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>The digest.</returns>
    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Whether a text is a lower- or upper-case 64-digit hexadecimal digest.
    /// </summary>
    /// <param name="text">Candidate digest.</param>
    /// <returns>True when well-formed.</returns>
    public static bool IsSha256Hex(string? text)
    {
        return text is { Length: 64 } && text.All(Uri.IsHexDigit);
    }

    #endregion

    #region Tools

    private static string Hash(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    #endregion
}
