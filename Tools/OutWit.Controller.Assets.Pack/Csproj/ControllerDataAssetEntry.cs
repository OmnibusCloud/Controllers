namespace OutWit.Controller.Assets.Pack.Csproj
{
    /// <summary>
    /// In-memory view of a single &lt;ControllerDataAsset&gt; declaration as read
    /// from a controller .csproj. Includes the consumer-facing metadata that
    /// flows into controller.json (Id, RuntimeIdentifier, Uri, Sha256, Size,
    /// ExtractTo, Required) plus author-side hints (PackSource, PackKind) that
    /// stay in the csproj only — the manifest emitter ignores them.
    /// </summary>
    public sealed class ControllerDataAssetEntry
    {
        #region Identity

        public required string Id { get; init; }

        public string RuntimeIdentifier { get; init; } = "any";

        #endregion

        #region Consumer manifest fields

        public required string Uri { get; init; }

        public string Sha256 { get; init; } = string.Empty;

        public long Size { get; init; }

        public string ExtractTo { get; init; } = ".";

        public bool Required { get; init; } = true;

        #endregion

        #region Author-side packing metadata

        /// <summary>
        /// Path relative to the prerequisites root. Required by the packer:
        /// for <see cref="PackKind"/> = zip-folder it must point at a directory;
        /// for single-file it points at the file to copy.
        /// </summary>
        public string? PackSource { get; init; }

        /// <summary>
        /// 'zip-folder' (recursive zip of a directory) or 'single-file'
        /// (copy a file verbatim into the output). Defaults to 'single-file'
        /// when omitted in the csproj.
        /// </summary>
        public string PackKind { get; init; } = PackKindSingleFile;

        public const string PackKindZipFolder = "zip-folder";

        public const string PackKindSingleFile = "single-file";

        #endregion
    }
}
