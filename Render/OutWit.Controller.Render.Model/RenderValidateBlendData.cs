using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Validation result for one uploaded .blend scene.
/// </summary>
[MemoryPackable]
public partial class RenderValidateBlendData : ModelBase
{
    #region Properties

    /// <summary>
    /// Whether the scene is currently considered portable and valid for the supported remote render flow.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation issues that block the current remote render flow.
    /// </summary>
    public List<string> Issues { get; set; } = [];

    /// <summary>
    /// Validation findings that do not currently block the remote render flow but may require user attention.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderValidateBlendData other)
            return false;

        return IsValid.Is(other.IsValid)
               && Issues.SequenceEqual(other.Issues)
               && Warnings.SequenceEqual(other.Warnings);
    }

    public override ModelBase Clone()
    {
        return new RenderValidateBlendData
        {
            IsValid = IsValid,
            Issues = [.. Issues],
            Warnings = [.. Warnings]
        };
    }

    #endregion
}
