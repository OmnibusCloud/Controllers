using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Tasks;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// The version-1 admission rules of a data scene (docs 06, part A) — checked on the node before
/// anything is materialized, and cheap enough for an initiator to mirror. Every member the composer
/// hands to pvpython is bounded here: one CalculiX result attachment on a logical path, an array name
/// that is a name (not a path, not code), a colour-map preset from the allowlist, defined enums.
/// </summary>
public static class ParaViewDataSceneValidator
{
    #region Constants

    /// <summary>Data file extension version 1 composes from.</summary>
    public const string FRD_EXTENSION = ".frd";

    /// <summary>Longest colour array name accepted.</summary>
    public const int MAX_ARRAY_NAME_CHARS = 128;

    /// <summary>Largest zero-based component index accepted (tensors carry at most nine).</summary>
    public const int MAX_COMPONENT_INDEX = 8;

    /// <summary>
    /// ParaView colour-map presets a data scene may name — every name verified against the bundled
    /// 6.1.1 runtime's preset vocabulary (vtkSMTransferFunctionPresets; the RealRuntime suite guards
    /// the list). An empty preset keeps ParaView's default.
    /// </summary>
    public static readonly IReadOnlySet<string> COLORMAP_PRESETS = new HashSet<string>(StringComparer.Ordinal)
    {
        "Cool to Warm",
        "Cool to Warm (Extended)",
        "Warm to Cool",
        "Warm to Cool (Extended)",
        "Rainbow Uniform",
        "Rainbow Desaturated",
        "Jet",
        "Turbo",
        "Viridis",
        "Inferno",
        "Magma",
        "Plasma",
        "Black-Body Radiation",
        "Grayscale",
        "X Ray",
        "Blue Orange (divergent)",
        "Blue - Green - Orange",
        "Linear Green (Gr4L)",
        "Linear Blue (8_31f)"
    };

    #endregion

    #region Functions

    /// <summary>
    /// Validates a data scene; every violation is appended to <paramref name="errors"/>.
    /// </summary>
    /// <param name="data">The data scene.</param>
    /// <param name="errors">Receives the violations (empty when the scene is admissible).</param>
    public static void Validate(ParaViewDataSceneData data, ICollection<string> errors)
    {
        if (data.Attachments.Count != 1)
            errors.Add($"a data scene carries exactly one attachment in version 1, got {data.Attachments.Count}");

        foreach (var attachment in data.Attachments)
            ValidateAttachment(attachment, errors);

        if (data.ColorArrayName.Length > MAX_ARRAY_NAME_CHARS)
            errors.Add($"colour array name exceeds {MAX_ARRAY_NAME_CHARS} characters");

        if (data.ColorArrayName.Any(char.IsControl))
            errors.Add("colour array name contains control characters");

        if (!Enum.IsDefined(data.ColorAssociation))
            errors.Add($"colour association {data.ColorAssociation} is not defined");

        if (data.ColorComponent < -1 || data.ColorComponent > MAX_COMPONENT_INDEX)
            errors.Add($"colour component {data.ColorComponent} is outside -1 (magnitude) .. {MAX_COMPONENT_INDEX}");

        if (data.ColormapPreset.Length > 0 && !COLORMAP_PRESETS.Contains(data.ColormapPreset))
            errors.Add($"colour-map preset '{data.ColormapPreset}' is not allowlisted");

        if (!Enum.IsDefined(data.Representation))
            errors.Add($"representation {data.Representation} is not defined");

        if (!Enum.IsDefined(data.CameraDirection))
            errors.Add($"camera direction {data.CameraDirection} is not defined");

        if (!Enum.IsDefined(data.FitTo))
            errors.Add($"camera fit {data.FitTo} is not defined");
    }

    #endregion

    #region Tools

    private static void ValidateAttachment(ParaViewAttachmentRefData attachment, ICollection<string> errors)
    {
        if (attachment.BlobId == Guid.Empty)
            errors.Add("attachment has no blob id");

        var pathViolation = ParaViewLogicalPath.Check(attachment.LogicalPath);
        if (pathViolation != null)
            errors.Add($"attachment: {pathViolation}");
        else if (!attachment.LogicalPath.EndsWith(FRD_EXTENSION, StringComparison.OrdinalIgnoreCase))
            errors.Add($"attachment '{attachment.LogicalPath}' is not a CalculiX {FRD_EXTENSION} result; version 1 composes {FRD_EXTENSION} only");

        if (attachment.Role != ParaViewAttachmentRole.ReaderInput)
            errors.Add($"attachment '{attachment.LogicalPath}' must be a reader input, got {attachment.Role}");

        if (attachment.Sha256.Length > 0 && !ParaViewPackageDigest.IsSha256Hex(attachment.Sha256))
            errors.Add($"attachment '{attachment.LogicalPath}' declares a malformed SHA-256");

        if (attachment.Size < 0)
            errors.Add($"attachment '{attachment.LogicalPath}' declares a negative size");

        if (attachment.SeriesGroup.Length > 0 || attachment.TimestepIndices.Count > 0)
            errors.Add($"attachment '{attachment.LogicalPath}' must be timestep-independent (no series group, no timestep indices)");
    }

    #endregion
}
