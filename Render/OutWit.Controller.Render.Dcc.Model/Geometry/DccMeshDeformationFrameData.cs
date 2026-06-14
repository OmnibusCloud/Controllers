using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// One baked deformation frame: the full set of vertex positions (aligned with the mesh's base
/// <see cref="DccMeshData.Positions"/>) at a given frame. Carrying deformation as a per-frame
/// vertex cache lets the neutral payload represent arbitrary deformation (skin, morph, cloth,
/// simulation) as the resulting geometry, independent of any source DCC's rig — so the same
/// contract works for exporters from other applications, not just 3ds Max.
/// </summary>
[MemoryPackable]
public partial class DccMeshDeformationFrameData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccMeshDeformationFrameData other
               && Frame.Is(other.Frame)
               && Positions.Count == other.Positions.Count
               && Positions.Zip(other.Positions, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new DccMeshDeformationFrameData
        {
            Frame = Frame,
            Positions = [.. Positions.Select(me => (DccVector3Data)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Frame number this deformed pose applies to.
    /// </summary>
    public int Frame { get; set; }

    /// <summary>
    /// Deformed vertex positions for this frame, aligned 1:1 with the mesh base positions.
    /// </summary>
    public List<DccVector3Data> Positions { get; set; } = [];

    #endregion
}
