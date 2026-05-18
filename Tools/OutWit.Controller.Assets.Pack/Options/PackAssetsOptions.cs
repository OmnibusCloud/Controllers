using CommandLine;

namespace OutWit.Controller.Assets.Pack.Options
{
    /// <summary>
    /// Command-line options for outwit-assets-pack.
    /// Parsed by CommandLineParser via <see cref="OutWit.Common.CommandLine.SerializationUtils"/>.
    /// </summary>
    public sealed class PackAssetsOptions
    {
        #region Inputs

        /// <summary>
        /// Path to the controller .csproj that declares ControllerDataAsset items.
        /// Required.
        /// </summary>
        [Value(0, MetaName = "csproj", Required = true,
            HelpText = "Path to the controller .csproj that declares <ControllerDataAsset> items.")]
        public string CsprojPath { get; set; } = string.Empty;

        /// <summary>
        /// Root directory containing the author-side binaries (e.g. WitEngine's
        /// @Prerequisites). Each ControllerDataAsset's PackSource metadata is
        /// interpreted relative to this root.
        /// </summary>
        [Option('p', "prerequisites", Required = true,
            HelpText = "Root directory for author-side asset sources. PackSource paths are resolved relative to this.")]
        public string Prerequisites { get; set; } = string.Empty;

        /// <summary>
        /// Where to drop the staged artifact files (zips + single-file copies)
        /// before they're uploaded to GitHub Release. Defaults to a sibling
        /// 'dist/&lt;tag&gt;/' next to the csproj.
        /// </summary>
        [Option('o', "output",
            HelpText = "Output directory for staged artifacts. Defaults to '<csproj-dir>/../../dist/<tag>/'.")]
        public string? Output { get; set; }

        #endregion

        #region Modes

        /// <summary>
        /// When set, rewrite the csproj in-place with new Sha256, Size, Uri,
        /// and Version values. Without it, the tool is a dry-run that prints
        /// the diff.
        /// </summary>
        [Option("apply",
            HelpText = "Rewrite the csproj in-place. Without it, the tool runs as dry-run.")]
        public bool Apply { get; set; }

        /// <summary>
        /// When set, wipe the output directory before staging. Otherwise an
        /// existing artifact file is reused (and re-hashed).
        /// </summary>
        [Option("clean",
            HelpText = "Wipe the output directory before staging fresh artifacts.")]
        public bool Clean { get; set; }

        #endregion

        #region Versioning

        /// <summary>
        /// Override the &lt;Version&gt; value to use. If omitted, the tool
        /// keeps the current version declared in the csproj.
        /// </summary>
        [Option('v', "version",
            HelpText = "Override the package version. If omitted, the existing <Version> in the csproj is kept.")]
        public string? Version { get; set; }

        /// <summary>
        /// Override the GH Release tag. If omitted, derived from the controller
        /// name + version (e.g. 'render-v1.15.2').
        /// </summary>
        [Option("release-tag",
            HelpText = "Override the GitHub Release tag. Derived as '<controller>-v<version>' if omitted.")]
        public string? ReleaseTag { get; set; }

        #endregion

        #region GitHub Release

        /// <summary>
        /// When set, after packing the tool creates (or updates) the GitHub
        /// Release matching <see cref="ReleaseTag"/> and uploads every staged
        /// artifact as a release asset.
        /// </summary>
        [Option("push-release",
            HelpText = "Create / update the matching GitHub Release and upload all staged artifacts.")]
        public bool PushRelease { get; set; }

        /// <summary>
        /// Name of the environment variable to read for the GitHub auth token.
        /// Default: GH_PACKAGE_TOKEN. Ignored if <see cref="Token"/> is set.
        /// </summary>
        [Option("token-env",
            HelpText = "Environment variable name to read for the GitHub auth token. Default: GH_PACKAGE_TOKEN.")]
        public string TokenEnv { get; set; } = "GH_PACKAGE_TOKEN";

        /// <summary>
        /// GitHub PAT passed inline. Overrides <see cref="TokenEnv"/>.
        /// Prefer the env-var path; this is for one-off / scripting use.
        /// </summary>
        [Option("token",
            HelpText = "GitHub PAT passed inline. Overrides --token-env. Prefer the env-var path for security.")]
        public string? Token { get; set; }

        /// <summary>
        /// GitHub owner/org for the release. Defaults to 'OmnibusCloud'.
        /// </summary>
        [Option("owner",
            HelpText = "GitHub owner / org. Default: OmnibusCloud.")]
        public string Owner { get; set; } = "OmnibusCloud";

        /// <summary>
        /// GitHub repository name. Defaults to 'Controllers'.
        /// </summary>
        [Option("repo",
            HelpText = "GitHub repository name. Default: Controllers.")]
        public string Repo { get; set; } = "Controllers";

        #endregion
    }
}
