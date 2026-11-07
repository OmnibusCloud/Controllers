using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// A scene to be COMPOSED on the fleet from bare data (docs 06, part A): one blob-referenced data
/// attachment plus the presentation choices a user makes in the GUI's first minute — what to
/// colour by, which colour map, surface or edges, whether to show the scalar bar, where the camera
/// looks from and which timesteps it must fit. <c>ParaView.Compose</c> turns it into an ordinary
/// <see cref="ParaViewSceneRefData"/> (a real saved state ParaView wrote itself) on one node, and
/// the unchanged validate → split → render chain takes it from there. Nothing here is a path, a
/// script, or a proxy definition: every member is a bounded value the composer maps onto ParaView
/// API calls, never onto code.
/// </summary>
[JobDocumentContract("paraview.dataScene@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewDataSceneData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewDataSceneData other)
            return false;

        return Attachments.Count == other.Attachments.Count
               && Attachments.Zip(other.Attachments, (left, right) => left.Is(right, tolerance)).All(me => me)
               && ColorArrayName.Is(other.ColorArrayName)
               && ColorAssociation.Is(other.ColorAssociation)
               && ColorComponent.Is(other.ColorComponent)
               && ColormapPreset.Is(other.ColormapPreset)
               && Representation.Is(other.Representation)
               && ShowScalarBar.Is(other.ShowScalarBar)
               && CameraDirection.Is(other.CameraDirection)
               && FitTo.Is(other.FitTo);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewDataSceneData
        {
            Attachments = [.. Attachments.Select(me => (ParaViewAttachmentRefData)me.Clone())],
            ColorArrayName = ColorArrayName,
            ColorAssociation = ColorAssociation,
            ColorComponent = ColorComponent,
            ColormapPreset = ColormapPreset,
            Representation = Representation,
            ShowScalarBar = ShowScalarBar,
            CameraDirection = CameraDirection,
            FitTo = FitTo
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The data the scene is composed from, as blob references with logical paths. Version 1 admits
    /// exactly one <see cref="ParaViewAttachmentRole.ReaderInput"/> CalculiX <c>.frd</c> result; its
    /// <see cref="ParaViewAttachmentRefData.Sha256"/> and <see cref="ParaViewAttachmentRefData.Size"/>
    /// may be left empty — the composer stamps the values it materialized.
    /// </summary>
    [MemoryPackOrder(0)]
    public List<ParaViewAttachmentRefData> Attachments { get; set; } = [];

    /// <summary>
    /// The data array to colour the surface by (for example <c>NDTEMP</c>, <c>DISP</c>, <c>STRESS</c>).
    /// Empty selects the first point array the data carries; a named array that the data does not
    /// carry fails the composition (the job reports the arrays that exist).
    /// </summary>
    [MemoryPackOrder(1)]
    public string ColorArrayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="ColorArrayName"/> names a point or a cell array.
    /// </summary>
    [MemoryPackOrder(2)]
    public ParaViewColorAssociation ColorAssociation { get; set; } = ParaViewColorAssociation.Points;

    /// <summary>
    /// The component of a multi-component array to colour by: -1 for the magnitude (the default),
    /// otherwise the zero-based component index.
    /// </summary>
    [MemoryPackOrder(3)]
    public int ColorComponent { get; set; } = -1;

    /// <summary>
    /// A ParaView colour-map preset name from the controller's allowlist (for example
    /// <c>Cool to Warm</c>, <c>Viridis (matplotlib)</c>, <c>Jet</c>); empty keeps ParaView's default.
    /// </summary>
    [MemoryPackOrder(4)]
    public string ColormapPreset { get; set; } = string.Empty;

    /// <summary>
    /// How the data is drawn.
    /// </summary>
    [MemoryPackOrder(5)]
    public ParaViewSceneRepresentation Representation { get; set; } = ParaViewSceneRepresentation.Surface;

    /// <summary>
    /// Whether the colour legend (scalar bar) of the coloured array is shown.
    /// </summary>
    [MemoryPackOrder(6)]
    public bool ShowScalarBar { get; set; } = true;

    /// <summary>
    /// The direction the camera looks from before it is fitted to the data.
    /// </summary>
    [MemoryPackOrder(7)]
    public ParaViewCameraDirection CameraDirection { get; set; } = ParaViewCameraDirection.Isometric;

    /// <summary>
    /// Which timesteps the camera (and the colour range) must accommodate.
    /// </summary>
    [MemoryPackOrder(8)]
    public ParaViewCameraFit FitTo { get; set; } = ParaViewCameraFit.AllTimesteps;

    #endregion
}
