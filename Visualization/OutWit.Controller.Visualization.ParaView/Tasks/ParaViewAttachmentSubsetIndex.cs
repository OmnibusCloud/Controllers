using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Tasks;

/// <summary>
/// One pass over the attachments: the always-included set and a timestep → attachments map, so a
/// package of many attachments and many timesteps splits in O(attachments + outputs) lookups.
/// Attachments keep their package order inside every subset.
/// </summary>
internal sealed class ParaViewAttachmentSubsetIndex
{
    #region Fields

    private readonly List<(int Order, ParaViewAttachmentRefData Attachment)> m_always = [];

    private readonly Dictionary<int, List<(int Order, ParaViewAttachmentRefData Attachment)>> m_byTimestep = new();

    #endregion

    #region Constructors

    public ParaViewAttachmentSubsetIndex(IEnumerable<ParaViewAttachmentRefData> attachments)
    {
        var order = 0;
        foreach (var attachment in attachments)
        {
            if (attachment.Role == ParaViewAttachmentRole.SeriesIndex || attachment.TimestepIndices.Count == 0)
            {
                m_always.Add((order, attachment));
            }
            else
            {
                foreach (var timestep in attachment.TimestepIndices.Distinct())
                {
                    if (!m_byTimestep.TryGetValue(timestep, out var list))
                    {
                        list = [];
                        m_byTimestep[timestep] = list;
                    }

                    list.Add((order, attachment));
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
}
