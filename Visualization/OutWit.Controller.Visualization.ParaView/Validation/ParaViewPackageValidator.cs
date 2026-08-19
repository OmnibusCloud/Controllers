using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Static pre-launch validation of a visualization package (docs 03, section 8.1), host-side, before
/// any large attachment is downloaded: the package reference, the attachments and their logical
/// paths, the runtime requirement, the output options, and the state itself — hardened XML parse,
/// proxy allowlist, programmable-pipeline rejection, file references against the package, views and
/// timeline. Produces the validation report ParaView.Split turns into tasks; every finding is a
/// permanent input failure, never a retry.
/// </summary>
public sealed class ParaViewPackageValidator
{
    #region Fields

    private readonly ParaViewProxyAllowlist m_allowlist;

    private readonly string? m_bundledReaderVersion;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a validator over an allowlist and the bundled reader version.
    /// </summary>
    /// <param name="allowlist">The proxy allowlist of the pinned runtime.</param>
    /// <param name="bundledReaderVersion">Version of the bundled reader, null when none is bundled.</param>
    public ParaViewPackageValidator(ParaViewProxyAllowlist allowlist, string? bundledReaderVersion)
    {
        m_allowlist = allowlist;
        m_bundledReaderVersion = bundledReaderVersion;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Validates a package against its output options, reading the state from a local file.
    /// </summary>
    /// <param name="scene">The package reference.</param>
    /// <param name="options">The output options.</param>
    /// <param name="statePath">Local path of the downloaded state file.</param>
    /// <returns>The report; <see cref="ParaViewValidationReportData.IsValid"/> is false when any error was found.</returns>
    public ParaViewValidationReportData Validate(ParaViewSceneRefData scene, ParaViewOutputOptionsData options, string statePath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var fallbacks = new List<string>();

        ValidateSceneReference(scene, errors);
        var pathSet = ValidateAttachments(scene, errors);
        ParaViewCompatibility.Check(scene.Runtime, m_bundledReaderVersion, errors, warnings);
        ValidateOptions(options, errors);

        var document = ValidateStateFile(scene, statePath, errors);

        var resolvedViewId = string.Empty;
        IReadOnlyList<double> timeline = scene.TimestepValues;
        IReadOnlyList<int> indices = [];
        var proxyTypes = new SortedSet<string>(StringComparer.Ordinal);

        if (document != null)
        {
            ValidateStateContent(document, scene, pathSet, errors, warnings, proxyTypes);
            resolvedViewId = ResolveView(document, options, errors, warnings);
            timeline = ResolveTimeline(document, scene, errors);
        }

        var timestepCount = Math.Max(1, timeline.Count);
        ValidateTimestepAssociations(scene, timestepCount, errors, fallbacks);

        if (errors.Count == 0)
            indices = ParaViewFrameSelectionResolver.Resolve(options.Frames, timestepCount, errors);

        return new ParaViewValidationReportData
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
            Fallbacks = fallbacks,
            PackageDigest = ParaViewPackageDigest.ComputePackageDigest(scene),
            ResolvedViewId = resolvedViewId,
            ResolvedTimestepIndices = [.. indices],
            TimestepValues = [.. timeline],
            AttachmentCount = scene.Attachments.Count,
            TotalAttachmentBytes = scene.StateSize + scene.Attachments.Sum(me => Math.Max(0, me.Size)),
            ProxyTypes = [.. proxyTypes],
            RequiredPlugins = [.. scene.Runtime.Plugins.Select(me => $"{me.Name}@{me.Version}")],
            RuntimeVersion = ParaViewRuntimeInfo.RUNTIME_VERSION,
            Width = options.Width,
            Height = options.Height,
            Format = options.Format
        };
    }

    #endregion

    #region Package

    private static void ValidateSceneReference(ParaViewSceneRefData scene, ICollection<string> errors)
    {
        if (scene.StateBlobId == Guid.Empty)
            errors.Add("scene reference has no state blob");

        if (!ParaViewPackageDigest.IsSha256Hex(scene.StateSha256))
            errors.Add("scene reference has no well-formed state SHA-256");

        if (scene.StateSize < 0)
            errors.Add("scene reference declares a negative state size");

        if (scene.PackageManifestJson.Length > ParaViewInputLimits.MAX_MANIFEST_CHARS)
            errors.Add($"package manifest exceeds {ParaViewInputLimits.MAX_MANIFEST_CHARS} characters");
    }

    private static HashSet<string> ValidateAttachments(ParaViewSceneRefData scene, ICollection<string> errors)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var collisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (scene.Attachments.Count > ParaViewInputLimits.MAX_ATTACHMENTS)
        {
            errors.Add($"package declares {scene.Attachments.Count} attachments, over the {ParaViewInputLimits.MAX_ATTACHMENTS} limit");
            return paths;
        }

        long totalBytes = Math.Max(0, scene.StateSize);

        foreach (var attachment in scene.Attachments)
        {
            var violation = ParaViewLogicalPath.Check(attachment.LogicalPath);
            if (violation != null)
            {
                errors.Add($"attachment: {violation}");
                continue;
            }

            if (!collisions.Add(attachment.LogicalPath))
                errors.Add($"attachment logical path '{attachment.LogicalPath}' is declared twice (paths are compared case-insensitively)");
            else
                paths.Add(attachment.LogicalPath);

            if (attachment.BlobId == Guid.Empty)
                errors.Add($"attachment '{attachment.LogicalPath}' has no blob");

            if (!ParaViewPackageDigest.IsSha256Hex(attachment.Sha256))
                errors.Add($"attachment '{attachment.LogicalPath}' has no well-formed SHA-256");

            if (attachment.Size < 0)
                errors.Add($"attachment '{attachment.LogicalPath}' declares a negative size");

            if (attachment.TimestepIndices.Any(index => index < 0))
                errors.Add($"attachment '{attachment.LogicalPath}' lists a negative timestep index");

            if (!Enum.IsDefined(attachment.Role))
                errors.Add($"attachment '{attachment.LogicalPath}' has an unknown role {attachment.Role}");

            totalBytes += Math.Max(0, attachment.Size);
        }

        if (totalBytes > ParaViewInputLimits.MAX_PACKAGE_BYTES)
            errors.Add($"package declares {totalBytes} bytes, over the {ParaViewInputLimits.MAX_PACKAGE_BYTES} byte limit");

        // A path cannot be both a file and a directory prefix of another file.
        var directoryPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in collisions)
        {
            var separator = path.LastIndexOf('/');
            while (separator > 0)
            {
                directoryPrefixes.Add(path[..separator]);
                separator = path.LastIndexOf('/', separator - 1);
            }
        }

        foreach (var path in collisions.Where(directoryPrefixes.Contains).OrderBy(me => me, StringComparer.Ordinal))
            errors.Add($"attachment logical path '{path}' is both a file and a directory of another attachment");

        return paths;
    }

    private static void ValidateOptions(ParaViewOutputOptionsData options, ICollection<string> errors)
    {
        var dimensions = ParaViewInputLimits.CheckDimensions(options.Width, options.Height);
        if (dimensions != null)
            errors.Add(dimensions);

        if (!Enum.IsDefined(options.Format))
            errors.Add($"unknown output format {options.Format}");

        if (options.ViewId.Length > ParaViewInputLimits.MAX_VIEW_ID_CHARS)
            errors.Add($"view id exceeds {ParaViewInputLimits.MAX_VIEW_ID_CHARS} characters");
    }

    private static void ValidateTimestepAssociations(ParaViewSceneRefData scene, int timestepCount, ICollection<string> errors, ICollection<string> fallbacks)
    {
        foreach (var attachment in scene.Attachments)
        {
            var outOfRange = attachment.TimestepIndices.Where(index => index >= timestepCount).ToList();
            if (outOfRange.Count > 0)
                errors.Add($"attachment '{attachment.LogicalPath}' is associated with timestep indices outside the timeline of {timestepCount} timestep(s): {string.Join(", ", outOfRange.Take(8))}");
        }

        foreach (var group in scene.Attachments
                     .Where(me => !string.IsNullOrEmpty(me.SeriesGroup))
                     .GroupBy(me => me.SeriesGroup, StringComparer.Ordinal)
                     .OrderBy(me => me.Key, StringComparer.Ordinal))
        {
            var members = group.Where(me => me.Role != ParaViewAttachmentRole.SeriesIndex).ToList();
            if (members.Count == 0 || members.Any(me => me.TimestepIndices.Count > 0))
                continue;

            fallbacks.Add($"series group '{group.Key}' ({members.Count} file(s), {members.Sum(me => Math.Max(0, me.Size))} bytes) carries no per-timestep association; the whole group ships to every task");
        }
    }

    #endregion

    #region State

    private static ParaViewStateDocument? ValidateStateFile(ParaViewSceneRefData scene, string statePath, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
        {
            errors.Add("state file could not be resolved from blob storage");
            return null;
        }

        var info = new FileInfo(statePath);

        if (scene.StateSize > 0 && info.Length != scene.StateSize)
            errors.Add($"state size mismatch: declared {scene.StateSize} bytes, blob holds {info.Length}");

        if (info.Length > ParaViewInputLimits.MAX_STATE_BYTES)
        {
            errors.Add($"state file is {info.Length} bytes, over the {ParaViewInputLimits.MAX_STATE_BYTES} byte limit");
            return null;
        }

        if (ParaViewPackageDigest.IsSha256Hex(scene.StateSha256))
        {
            var actual = ParaViewPackageDigest.HashFile(statePath);
            if (!string.Equals(actual, scene.StateSha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("state digest mismatch: the blob does not match the declared SHA-256");
                return null;
            }
        }

        try
        {
            return ParaViewStateDocument.Parse(statePath);
        }
        catch (ParaViewStateFormatException e)
        {
            errors.Add(e.Message);
            return null;
        }
    }

    private void ValidateStateContent(
        ParaViewStateDocument document,
        ParaViewSceneRefData scene,
        IReadOnlySet<string> packagePaths,
        ICollection<string> errors,
        ICollection<string> warnings,
        ISet<string> proxyTypes)
    {
        if (document.HasCustomProxyDefinitions)
            errors.Add("state embeds custom proxy definitions, which version 1 does not admit");

        if (!string.IsNullOrEmpty(document.Version)
            && ParaViewCompatibility.TryParseVersion(document.Version, out var stateMajor, out var stateMinor)
            && (stateMajor != scene.Runtime.ParaViewMajor || stateMinor != scene.Runtime.ParaViewMinor))
            warnings.Add($"state was saved by ParaView {document.Version} while the package declares {scene.Runtime.ParaViewMajor}.{scene.Runtime.ParaViewMinor}");

        var requiredPlugins = scene.Runtime.Plugins.Select(me => me.Name).ToList();
        var notAllowed = new SortedSet<string>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var proxy in document.Proxies)
        {
            proxyTypes.Add(proxy.Key);

            if (ParaViewProxyPolicy.BLOCKED_PROXY_TYPES.Contains(proxy.Type))
            {
                errors.Add($"state instantiates '{proxy.Key}' (id {proxy.Id}), which executes user code and is rejected");
                continue;
            }

            foreach (var property in proxy.Properties.Where(ParaViewProxyPolicy.IsBlockedProperty))
                errors.Add($"proxy '{proxy.Key}' (id {proxy.Id}) carries executable property '{property.Name}' and is rejected");

            if (!m_allowlist.Allows(proxy.Key, requiredPlugins))
                notAllowed.Add(proxy.Key);

            foreach (var property in proxy.Properties.Where(property => ParaViewProxyPolicy.IsFileProperty(proxy, property)))
            {
                foreach (var value in property.Values.Where(value => !string.IsNullOrEmpty(value)))
                {
                    var violation = ParaViewLogicalPath.Check(value);
                    if (violation != null)
                    {
                        errors.Add($"proxy '{proxy.Key}' (id {proxy.Id}) property '{property.Name}': {violation}");
                        continue;
                    }

                    if (!packagePaths.Contains(value))
                    {
                        errors.Add($"proxy '{proxy.Key}' (id {proxy.Id}) property '{property.Name}' references '{value}', which is not an attachment of the package");
                        continue;
                    }

                    referenced.Add(value);
                }
            }
        }

        foreach (var key in notAllowed)
            errors.Add($"state instantiates '{key}', which is not in the proxy allowlist of ParaView {m_allowlist.RuntimeVersion}");

        foreach (var attachment in scene.Attachments)
        {
            if (attachment.Role == ParaViewAttachmentRole.ReaderInput
                && string.IsNullOrEmpty(attachment.SeriesGroup)
                && !referenced.Contains(attachment.LogicalPath))
                warnings.Add($"attachment '{attachment.LogicalPath}' is not referenced by any reader in the state");
        }
    }

    private static string ResolveView(ParaViewStateDocument document, ParaViewOutputOptionsData options, ICollection<string> errors, ICollection<string> warnings)
    {
        var views = document.ViewNames;
        if (views.Count == 0)
        {
            errors.Add("state registers no views");
            return string.Empty;
        }

        if (string.IsNullOrEmpty(options.ViewId))
        {
            if (views.Count > 1)
                warnings.Add($"state registers {views.Count} views and no view id was requested; rendering '{views[0]}'");

            return views[0];
        }

        if (!views.Contains(options.ViewId, StringComparer.Ordinal))
        {
            errors.Add($"requested view '{options.ViewId}' is not registered in the state (views: {string.Join(", ", views)})");
            return string.Empty;
        }

        return options.ViewId;
    }

    private static IReadOnlyList<double> ResolveTimeline(ParaViewStateDocument document, ParaViewSceneRefData scene, ICollection<string> errors)
    {
        IReadOnlyList<double>? stateTimeline;
        try
        {
            stateTimeline = document.TimestepValues;
        }
        catch (ParaViewStateFormatException e)
        {
            errors.Add(e.Message);
            return scene.TimestepValues;
        }

        if (stateTimeline == null)
            return scene.TimestepValues;

        if (scene.TimestepValues.Count > 0 && scene.TimestepValues.Count != stateTimeline.Count)
            errors.Add($"timeline mismatch: the package declares {scene.TimestepValues.Count} timesteps, the state's TimeKeeper carries {stateTimeline.Count}");

        return stateTimeline;
    }

    #endregion
}
