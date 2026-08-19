using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Tasks;

/// <summary>
/// One pass over the attachments: the always-included set (statics, series indexes, series anchors)
/// and a timestep → attachments map, so a package of many attachments and many timesteps splits in
/// O(attachments + outputs) lookups. Attachments keep their package order inside every subset.
/// The <b>series anchor</b> is the first member of every series group (lowest ordinal, then package
/// order): ParaView's PVD and file-series readers open the first file of a series when the state
/// loads, whatever timestep is rendered — verified against ParaView 6.1.1 — so it ships with every
/// task at the cost of one extra piece per task, keeping per-task data O(1) in the series length.
/// </summary>
public sealed class ParaViewAttachmentSubsetIndex
{
    #region Fields

    private readonly List<(int Order, ParaViewAttachmentRefData Attachment)> m_always = [];

    private readonly Dictionary<int, List<(int Order, ParaViewAttachmentRefData Attachment)>> m_byTimestep = new();

    private readonly List<string> m_anchors = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Indexes the attachments of one package.
    /// </summary>
    /// <param name="attachments">All package attachments in package order.</param>
    public ParaViewAttachmentSubsetIndex(IEnumerable<ParaViewAttachmentRefData> attachments)
    {
        var list = attachments.ToList();

        var anchors = new HashSet<ParaViewAttachmentRefData>(ReferenceEqualityComparer.Instance);
        foreach (var group in list
                     .Select((attachment, position) => (attachment, position))
                     .Where(me => !string.IsNullOrEmpty(me.attachment.SeriesGroup) && me.attachment.Role != ParaViewAttachmentRole.SeriesIndex)
                     .GroupBy(me => me.attachment.SeriesGroup, StringComparer.Ordinal))
        {
            var anchor = group.OrderBy(me => me.attachment.SeriesOrdinal).ThenBy(me => me.position).First().attachment;
            anchors.Add(anchor);
            m_anchors.Add(anchor.LogicalPath);
        }

        var order = 0;
        foreach (var attachment in list)
        {
            if (attachment.Role == ParaViewAttachmentRole.SeriesIndex || attachment.TimestepIndices.Count == 0 || anchors.Contains(attachment))
            {
                m_always.Add((order, attachment));
            }
            else
            {
                foreach (var timestep in attachment.TimestepIndices.Distinct())
                {
                    if (!m_byTimestep.TryGetValue(timestep, out var members))
                    {
                        members = [];
                        m_byTimestep[timestep] = members;
                    }

                    members.Add((order, attachment));
                }
            }

            order++;
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// The minimal subset of one timestep: the always-included attachments plus the timestep's own, in package order.
    /// </summary>
    /// <param name="timestepIndex">The task's timestep index.</param>
    /// <returns>Cloned attachment references.</returns>
    public List<ParaViewAttachmentRefData> SubsetOf(int timestepIndex)
    {
        IEnumerable<(int Order, ParaViewAttachmentRefData Attachment)> members = m_always;
        if (m_byTimestep.TryGetValue(timestepIndex, out var own))
            members = members.Concat(own);

        return members
            .OrderBy(me => me.Order)
            .Select(me => (ParaViewAttachmentRefData)me.Attachment.Clone())
            .ToList();
    }

    #endregion

    #region Properties

    /// <summary>Logical paths of the series anchors (one per series group), in package order.</summary>
    public IReadOnlyList<string> Anchors => m_anchors;

    #endregion
}
