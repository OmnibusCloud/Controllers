using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OutWit.Controller.Assets.Pack.Csproj
{
    /// <summary>
    /// Loads a controller .csproj, exposes its &lt;ControllerDataAsset&gt; items
    /// + top-level &lt;Version&gt; / &lt;ControllerName&gt;, and rewrites the
    /// mutable fields (Sha256, Size, Uri, top-level Version) preserving the
    /// rest of the file character-for-character.
    /// </summary>
    /// <remarks>
    /// Reading uses LINQ-to-XML for clean structural access. Writing is
    /// line-based regex replacement so indentation, line endings (CRLF/LF),
    /// comments, attribute order, and trailing whitespace round-trip
    /// untouched. XDocument.Save() can normalise these subtly, which would
    /// produce noisy git diffs every time the tool runs.
    /// </remarks>
    public sealed class ControllerDataAssetEditor
    {
        #region Fields

        private readonly string m_originalText;

        #endregion

        #region Constructor

        private ControllerDataAssetEditor(
            string csprojPath,
            string originalText,
            string controllerName,
            string version,
            IReadOnlyList<ControllerDataAssetEntry> entries)
        {
            CsprojPath = csprojPath;
            m_originalText = originalText;
            ControllerName = controllerName;
            Version = version;
            Entries = entries;
        }

        #endregion

        #region Properties

        public string CsprojPath { get; }

        public string ControllerName { get; }

        public string Version { get; }

        public IReadOnlyList<ControllerDataAssetEntry> Entries { get; }

        #endregion

        #region Methods (load)

        public static ControllerDataAssetEditor Load(string csprojPath)
        {
            if (!File.Exists(csprojPath))
                throw new FileNotFoundException($"Csproj not found: '{csprojPath}'.", csprojPath);

            var text = File.ReadAllText(csprojPath);
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);

            var controllerName = ReadFirstElementValue(doc, "ControllerName")
                ?? throw new InvalidOperationException(
                    $"Csproj '{csprojPath}' has no <ControllerName>. " +
                    $"This tool only operates on controller projects that follow the shared Build/ pattern.");

            var version = ReadFirstElementValue(doc, "Version")
                ?? throw new InvalidOperationException(
                    $"Csproj '{csprojPath}' has no top-level <Version>.");

            var entries = doc.Descendants("ControllerDataAsset")
                .Select(BuildEntry)
                .ToList();

            return new ControllerDataAssetEditor(csprojPath, text, controllerName, version, entries);
        }

        private static string? ReadFirstElementValue(XDocument doc, string elementName)
        {
            return doc.Descendants(elementName).FirstOrDefault()?.Value.Trim();
        }

        private static ControllerDataAssetEntry BuildEntry(XElement el)
        {
            var id = (string?)el.Attribute("Include")
                ?? throw new InvalidOperationException(
                    "Encountered a <ControllerDataAsset> with no Include= attribute.");

            string Child(string name) => el.Element(name)?.Value.Trim() ?? string.Empty;
            string? ChildOrNull(string name) => el.Element(name)?.Value.Trim();
            bool ChildBoolOrTrue(string name)
            {
                var raw = ChildOrNull(name);
                return raw == null || !bool.TryParse(raw, out var b) || b;
            }
            long ChildLongOrZero(string name)
            {
                var raw = ChildOrNull(name);
                return raw != null && long.TryParse(raw, out var n) ? n : 0;
            }

            return new ControllerDataAssetEntry
            {
                Id                = id,
                RuntimeIdentifier = string.IsNullOrEmpty(Child("RuntimeIdentifier")) ? "any" : Child("RuntimeIdentifier"),
                Uri               = Child("Uri"),
                Sha256            = Child("Sha256"),
                Size              = ChildLongOrZero("Size"),
                ExtractTo         = string.IsNullOrEmpty(Child("ExtractTo")) ? "." : Child("ExtractTo"),
                Required          = ChildBoolOrTrue("Required"),
                PackSource        = ChildOrNull("PackSource"),
                PackKind          = string.IsNullOrEmpty(Child("PackKind")) ? ControllerDataAssetEntry.PackKindSingleFile : Child("PackKind"),
            };
        }

        #endregion

        #region Methods (write)

        /// <summary>
        /// Returns the csproj text with the requested updates applied. Does not
        /// touch disk — caller decides when to <see cref="Save"/>. Each entry in
        /// <paramref name="updates"/> is keyed by ControllerDataAsset.Id.
        /// </summary>
        public string Apply(IReadOnlyDictionary<string, AssetUpdate> updates, string? newVersion = null)
        {
            if (updates is null)
                throw new ArgumentNullException(nameof(updates));

            // Parts captures each newline match in the split result so we can
            // reconstruct CRLF/LF exactly as the source used them.
            var parts = Regex.Split(m_originalText, "(\r?\n)");
            var sb = new System.Text.StringBuilder(m_originalText.Length + 256);

            string? currentAssetId = null;
            var versionRewritten = false;

            foreach (var line in parts)
            {
                var rewritten = line;

                var openMatch = OpenTagRegex.Match(line);
                if (openMatch.Success)
                {
                    currentAssetId = openMatch.Groups[1].Value;
                }
                else if (CloseTagRegex.IsMatch(line))
                {
                    currentAssetId = null;
                }
                else if (currentAssetId is not null && updates.TryGetValue(currentAssetId, out var u))
                {
                    rewritten = RewriteAssetField(line, u);
                }
                else if (!versionRewritten && currentAssetId is null && newVersion is not null && VersionTagRegex.IsMatch(line))
                {
                    rewritten = VersionTagRegex.Replace(line, $"<Version>{newVersion}</Version>", 1);
                    versionRewritten = true;
                }

                sb.Append(rewritten);
            }

            return sb.ToString();
        }

        private static string RewriteAssetField(string line, AssetUpdate u)
        {
            if (UriTagRegex.IsMatch(line))
                return UriTagRegex.Replace(line, $"<Uri>{EscapeXml(u.Uri)}</Uri>", 1);

            if (Sha256TagRegex.IsMatch(line))
                return Sha256TagRegex.Replace(line, $"<Sha256>{u.Sha256}</Sha256>", 1);

            if (SizeTagRegex.IsMatch(line))
                return SizeTagRegex.Replace(line, $"<Size>{u.Size}</Size>", 1);

            return line;
        }

        public void Save(string newText)
        {
            File.WriteAllText(CsprojPath, newText);
        }

        #endregion

        #region Helpers

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static readonly Regex OpenTagRegex    = new("<ControllerDataAsset Include=\"([^\"]+)\">", RegexOptions.Compiled);
        private static readonly Regex CloseTagRegex   = new("</ControllerDataAsset>",                     RegexOptions.Compiled);
        private static readonly Regex UriTagRegex     = new("<Uri>[^<]*</Uri>",                           RegexOptions.Compiled);
        private static readonly Regex Sha256TagRegex  = new("<Sha256>[^<]*</Sha256>",                     RegexOptions.Compiled);
        private static readonly Regex SizeTagRegex    = new("<Size>[^<]*</Size>",                         RegexOptions.Compiled);
        private static readonly Regex VersionTagRegex = new("<Version>[^<]*</Version>",                   RegexOptions.Compiled);

        #endregion
    }

    /// <summary>
    /// New SHA / Size / Uri triplet to write into a single ControllerDataAsset.
    /// </summary>
    public readonly record struct AssetUpdate(string Sha256, long Size, string Uri);
}
